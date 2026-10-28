using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Reyma.VectorSpace.Engine.Microservice.Api;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Item 1.19, criterio literal: <c>GET .../documentos</c> trae ruta, huella (document_fingerprint) y cantidad de
/// puntos de cada documento indexado, directo de Qdrant, sin tocar <see cref="IMotorDeIngestaProyecto"/> ni el
/// barrido de ausentes. Contra Qdrant y TEI reales de docker-compose.motor.yml (mismos puertos que
/// <see cref="Motor.MotorIntegracionTests"/>/<see cref="IngestaProyectoZipIntegracionTests"/>).
/// </summary>
[Collection("Api")]
public sealed class DocumentosIndexadosIntegracionTests(ApiFactory factory)
{
    private const string ClaseA = """
        namespace Demo.Pedidos;

        public class ServicioPedidos
        {
            public decimal CalcularTotal(IEnumerable<decimal> lineas)
            {
                var total = 0m;
                foreach (var linea in lineas) total += linea;
                return total;
            }
        }
        """;

    private const string ClaseAEditada = """
        namespace Demo.Pedidos;

        public class ServicioPedidos
        {
            public decimal CalcularTotal(IEnumerable<decimal> lineas)
            {
                var total = 0m;
                foreach (var linea in lineas) total += linea;
                return total * 1.0m;
            }

            public bool TieneLineas(IEnumerable<decimal> lineas) => lineas.Any();
        }
        """;

    private const string GuiaB = """
        # Guia de pedidos

        ## Cancelacion

        Un pedido se puede cancelar durante las primeras veinticuatro horas desde su creacion, sin costo para el cliente.
        """;

    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");
    private readonly QdrantClient _qdrant = new("127.0.0.1", 16334);

    private WebApplicationFactory<Program> Api(params string[] colecciones)
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Qdrant:PuertoGrpc"] = "16334",
            ["Motor:Qdrant:PuertoRest"] = "16333",
            ["Motor:Embeddings:UrlBase"] = "http://127.0.0.1:18080",
        };
        return factory.ConIdentidad(config);
    }

    private static string NuevaColeccion() => "it-1-19-" + Guid.NewGuid().ToString("N")[..10];

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    private static async Task<JArray> GetDocumentos(HttpClient client, string coleccion)
    {
        using var response = await client.GetAsync($"/api/colecciones/{coleccion}/documentos");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JArray.Parse(await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> Ingerir(HttpClient client, string coleccion, params (string Ruta, string Contenido)[] documentos) =>
        client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
        {
            origen = new
            {
                tipo = "documentos",
                documentos = documentos.Select(d => new { ruta = d.Ruta, contenido = d.Contenido }).ToArray()
            }
        });

    private async Task<int> PuntosDirectoEnQdrant(string coleccion, string filePath)
    {
        var r = await _qdrant.ScrollAsync(coleccion, limit: 1000,
            payloadSelector: new Qdrant.Client.Grpc.WithPayloadSelector
            {
                Include = new Qdrant.Client.Grpc.PayloadIncludeSelector { Fields = { "file_path" } }
            });
        return r.Result.Count(p => p.Payload["file_path"].StringValue == filePath);
    }

    [RequiereMotorLocalFact]
    public async Task ListaNDocumentosConSusPuntosYLaHuellaCambiaAlEditarYDesaparecenAlBorrar()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            // 1. Ingesta inicial de 2 documentos: el listado debe traer los 2, con el mismo conteo de puntos
            //    que un scroll directo a Qdrant.
            using (var r = await Ingerir(client, coleccion, ("src/ServicioPedidos.cs", ClaseA), ("docs/guia.md", GuiaB)))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            var listado = await GetDocumentos(client, coleccion);
            Assert.Equal(2, listado.Count);

            var filaA = listado.Single(d => (string)d["ruta"]! == "src/ServicioPedidos.cs");
            var filaB = listado.Single(d => (string)d["ruta"]! == "docs/guia.md");
            Assert.NotNull((string?)filaA["huella"]);
            Assert.NotNull((string?)filaB["huella"]);
            var huellaInicialA = (string)filaA["huella"]!;

            Assert.Equal(await PuntosDirectoEnQdrant(coleccion, $"{coleccion}/src/ServicioPedidos.cs"), (int)filaA["puntos"]!);
            Assert.Equal(await PuntosDirectoEnQdrant(coleccion, $"{coleccion}/docs/guia.md"), (int)filaB["puntos"]!);

            // 2. Editar un documento y reingerir: solo cambia su huella; sigue siendo la misma ruta, no aparece
            //    como documento nuevo, y el total de documentos no cambia.
            using (var r = await Ingerir(client, coleccion, ("src/ServicioPedidos.cs", ClaseAEditada)))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            var listadoTrasEditar = await GetDocumentos(client, coleccion);
            Assert.Equal(2, listadoTrasEditar.Count);
            var filaAEditada = listadoTrasEditar.Single(d => (string)d["ruta"]! == "src/ServicioPedidos.cs");
            var huellaTrasEditar = (string)filaAEditada["huella"]!;
            Assert.NotEqual(huellaInicialA, huellaTrasEditar);
            Assert.Equal(await PuntosDirectoEnQdrant(coleccion, $"{coleccion}/src/ServicioPedidos.cs"), (int)filaAEditada["puntos"]!);

            var filaBSinCambios = listadoTrasEditar.Single(d => (string)d["ruta"]! == "docs/guia.md");
            Assert.Equal((string)filaB["huella"]!, (string)filaBSinCambios["huella"]!);

            // 3. "Borrar" el documento editado (contenido vacio, mismo patron que el origen 'documentos' de 1.7.2):
            //    desaparece del listado, el otro sigue.
            using (var r = await Ingerir(client, coleccion, ("src/ServicioPedidos.cs", "")))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            var listadoFinal = await GetDocumentos(client, coleccion);
            Assert.Single(listadoFinal);
            Assert.Equal("docs/guia.md", (string)listadoFinal[0]["ruta"]!);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task ColeccionInexistenteResponde200ConListaVacia()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);

        var listado = await GetDocumentos(client, coleccion);
        Assert.Empty(listado);
    }
}
