using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// 1.7.4: perfiles de embeddings y metadata de coleccion en Qdrant. Contra el Qdrant y el TEI reales de
/// docker-compose.motor.yml: el perfil adicional de prueba ("perfil-alterno-256") apunta al mismo TEI local pero con
/// otro nombre de modelo, para reproducir la mezcla de perfiles de 1.7.3 (misma dimension, perfil distinto) sin
/// depender de AltheIA real. Como la verificacion de perfil ocurre antes de llamar al sidecar (DocumentIngestionPipeline
/// paso 2, antes del paso 4 de vectorizar), el 409 se da sin que TEI llegue a ver la solicitud con el modelo ajeno.
/// </summary>
[Collection("Api")]
public sealed class PerfilesEmbeddingsIntegracionTests(ApiFactory factory)
{
    private const string PerfilAlterno = "perfil-alterno-256";
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");
    private readonly QdrantClient _qdrant = new("127.0.0.1", 16334);

    private WebApplicationFactory<Program> Api(params string[] colecciones)
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Qdrant:PuertoGrpc"] = "16334",
            ["Motor:Qdrant:PuertoRest"] = "16333",
            ["Motor:Embeddings:UrlBase"] = "http://127.0.0.1:18080",
            // Mismo servidor TEI que el perfil por defecto, pero con otro nombre de perfil/modelo y la misma
            // dimension: basta para reproducir el hueco de 1.7.3 (perfil distinto, dimension igual) sin AltheIA real.
            [$"Motor:Embeddings:Perfiles:{PerfilAlterno}:UrlBase"] = "http://127.0.0.1:18080",
            [$"Motor:Embeddings:Perfiles:{PerfilAlterno}:Modelo"] = "modelo-alterno-de-prueba",
            [$"Motor:Embeddings:Perfiles:{PerfilAlterno}:Dimension"] = "256",
        };
        return factory.ConIdentidad(config);
    }

    private static string NuevaColeccion() => "it-perfil-" + Guid.NewGuid().ToString("N")[..10];

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    private static async Task<HttpResponseMessage> Ingerir(HttpClient client, string coleccion, string ruta, string contenido, string? perfil = null)
    {
        object cuerpo = perfil is null
            ? new { origen = new { tipo = "documentos", documentos = new[] { new { ruta, contenido } } } }
            : new { origen = new { tipo = "documentos", documentos = new[] { new { ruta, contenido } } }, perfil };
        return await client.PostJson($"/api/colecciones/{coleccion}/ingestas", cuerpo);
    }

    private const string DocA = """
        # Nota A

        Contenido suficientemente largo para superar el minimo indexable del pipeline de ingesta del servicio.
        """;

    private const string DocB = """
        # Nota B

        Otro contenido suficientemente largo para superar el minimo indexable del pipeline de ingesta del servicio.
        """;

    [RequiereMotorLocalFact]
    public async Task UnaColeccionNuevaGuardaElPerfilModeloYDimensionEnSuMetadata()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            using var respuesta = await Ingerir(client, coleccion, "a.md", DocA);
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

            using var info = await new HttpClient { BaseAddress = new Uri("http://127.0.0.1:16333/") }
                .GetAsync($"collections/{coleccion}");
            var json = JObject.Parse(await info.Content.ReadAsStringAsync());
            var metadata = json["result"]!["config"]!["metadata"]!;
            Assert.Equal("tei-arctic-256", (string)metadata["perfil_embeddings"]!);
            Assert.Equal("Snowflake/snowflake-arctic-embed-m-v2.0", (string)metadata["modelo"]!);
            Assert.Equal(256, (int)metadata["dimension"]!);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task UnPerfilDistintoDeLaMismaDimensionResponde409SinEscribir()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            using var primera = await Ingerir(client, coleccion, "a.md", DocA);
            Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
            var puntosTrasPrimera = await _qdrant.CountAsync(coleccion);

            // Mezcla de 1.7.3: mismo campo "dimension" (256), perfil distinto -> 409, no 200.
            using var mezcla = await Ingerir(client, coleccion, "b.md", DocB, perfil: PerfilAlterno);
            Assert.Equal(HttpStatusCode.Conflict, mezcla.StatusCode);
            var cuerpo = await mezcla.Content.ReadAsStringAsync();
            Assert.Contains("tei-arctic-256", cuerpo);
            Assert.Contains(PerfilAlterno, cuerpo);

            // No escribio nada: mismos puntos que tras la primera ingesta.
            Assert.Equal(puntosTrasPrimera, await _qdrant.CountAsync(coleccion));

            // La busqueda con el perfil alterno sobre la misma coleccion tambien es 409, no una busqueda con el
            // modelo equivocado.
            using var busqueda = await client.PostJson($"/api/colecciones/{coleccion}/busquedas",
                new { consulta = "nota", perfil = PerfilAlterno });
            Assert.Equal(HttpStatusCode.Conflict, busqueda.StatusCode);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task UnaColeccionSinMetadataSeAsumeDelPerfilPorDefectoYSeLeEscribeLaMetadata()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            // Coleccion creada directamente en Qdrant (como antes de 1.7.4: sin metadata de perfil), con el mismo
            // esquema dense+sparse que crea el servicio y la misma dimension que el perfil por defecto.
            var vectores = new VectorParamsMap();
            vectores.Map["dense"] = new VectorParams { Size = 256, Distance = Distance.Cosine };
            var sparse = new SparseVectorConfig();
            sparse.Map["sparse-code"] = new SparseVectorParams { Index = new SparseIndexConfig { FullScanThreshold = 5000 } };
            await _qdrant.CreateCollectionAsync(coleccion, vectorsConfig: vectores, sparseVectorsConfig: sparse);

            // El perfil alterno (otra dimension logica aunque el numero coincida) se rechaza: la coleccion sin
            // metadata se asume del perfil por defecto, no del que pida la solicitud.
            using var conPerfilAlterno = await Ingerir(client, coleccion, "a.md", DocA, perfil: PerfilAlterno);
            Assert.Equal(HttpStatusCode.Conflict, conPerfilAlterno.StatusCode);
            Assert.Equal(0ul, await _qdrant.CountAsync(coleccion));

            // Con el perfil por defecto (implicito) si entra, y de paso le escribe la metadata que le faltaba.
            using var conDefecto = await Ingerir(client, coleccion, "a.md", DocA);
            Assert.True(conDefecto.StatusCode == HttpStatusCode.OK, $"{(int)conDefecto.StatusCode}: {await conDefecto.Content.ReadAsStringAsync()}");

            using var info = await new HttpClient { BaseAddress = new Uri("http://127.0.0.1:16333/") }
                .GetAsync($"collections/{coleccion}");
            var metadata = JObject.Parse(await info.Content.ReadAsStringAsync())["result"]!["config"]!["metadata"];
            Assert.Equal("tei-arctic-256", (string)metadata!["perfil_embeddings"]!);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task UnPerfilNoRegistradoResponde400ConLosPerfilesDisponibles()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            using var respuesta = await Ingerir(client, coleccion, "a.md", DocA, perfil: "perfil-que-no-existe");
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var cuerpo = await respuesta.Content.ReadAsStringAsync();
            Assert.Contains("perfil-que-no-existe", cuerpo);
            Assert.Contains("tei-arctic-256", cuerpo);
            Assert.Contains(PerfilAlterno, cuerpo);
            Assert.False(await _qdrant.CollectionExistsAsync(coleccion), "Un perfil no registrado no debe crear la coleccion.");
        }
        finally
        {
            await Borrar(coleccion);
        }
    }
}
