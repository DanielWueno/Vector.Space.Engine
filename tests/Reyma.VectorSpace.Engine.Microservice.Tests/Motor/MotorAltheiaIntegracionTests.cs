using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Solo con <c>VSE_ALTHEIA_LOCAL=1</c>, <c>ALTHEIA_KEY</c> y <c>ALTHEIA_CERT</c> definidas (variables de usuario), y
/// docker-compose.motor.yml levantado para Qdrant (16334). No usa ningun TEI local: habla con el servidor de IA
/// corporativo AltheIA real (bge-m3, perfil temporal de 1.13), consumiendo una cantidad minima declarada de su
/// cuota diaria. Colecciones aisladas y descartables, borradas al terminar cada prueba.
/// </summary>
public sealed class RequiereAltheiaFactAttribute : FactAttribute
{
    public const string Variable = "VSE_ALTHEIA_LOCAL";

    public RequiereAltheiaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip = $"Requiere {Variable}=1, ALTHEIA_KEY y ALTHEIA_CERT definidas, y docker-compose.motor.yml levantado (Qdrant 127.0.0.1:16334).";
            return;
        }
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALTHEIA_KEY")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALTHEIA_CERT")))
            Skip = "VSE_ALTHEIA_LOCAL=1 pero faltan ALTHEIA_KEY o ALTHEIA_CERT.";
    }
}

[Collection("Api")]
public sealed class MotorAltheiaIntegracionTests(ApiFactory factory)
{
    // Documento minimo a proposito: el objetivo es verificar el perfil de conexion, no medir calidad de busqueda,
    // y AltheIA cuenta contra una cuota diaria compartida (1.7, hallazgo 2026-10-22).
    private const string DocumentoChico = """
        # Nota de prueba

        Esta es una nota minima para verificar la conexion del motor con AltheIA.
        """;

    private readonly QdrantClient _qdrant = new("127.0.0.1", 16334);

    private WebApplicationFactory<Program> Api(string coleccion, int dimension = 1024)
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Qdrant:PuertoGrpc"] = "16334",
            ["Motor:Qdrant:PuertoRest"] = "16333",
            ["Motor:Embeddings:UrlBase"] = "https://altheia",
            ["Motor:Embeddings:Ruta"] = "v1/embeddings",
            ["Motor:Embeddings:Modelo"] = "bge-m3",
            ["Motor:Embeddings:Dimension"] = dimension.ToString(),
            ["Motor:Embeddings:PrefijoConsulta"] = "",
            ["Motor:Embeddings:ApiKey"] = Environment.GetEnvironmentVariable("ALTHEIA_KEY"),
            ["Motor:Embeddings:RutaCertificadoCA"] = Environment.GetEnvironmentVariable("ALTHEIA_CERT"),
            ["ControlDeAcceso:Concesiones:0:Coleccion"] = coleccion,
            ["ControlDeAcceso:Concesiones:0:Operaciones:0"] = "Consultar",
            ["ControlDeAcceso:Concesiones:0:Operaciones:1"] = "Ingestar",
            ["ControlDeAcceso:Concesiones:0:Usuarios:0"] = "ana",
        };
        return factory.ConIdentidad(config);
    }

    private static string NuevaColeccion() => "it-altheia-" + Guid.NewGuid().ToString("N")[..10];

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    [RequiereAltheiaFact]
    public async Task IngestaBusquedaYRepeticionContraAltheiaReal()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(Token(sub: IdentidadDePrueba.SubAna, usuario: "ana"));
        try
        {
            using var primera = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos = new[] { new { ruta = "docs/nota.md", contenido = DocumentoChico } } }
            });
            Assert.True(primera.IsSuccessStatusCode, $"{(int)primera.StatusCode}: {await primera.Content.ReadAsStringAsync()}");
            var cuerpo1 = await primera.Leer<JObject>();
            Assert.Equal(1, (int)cuerpo1["documentosIndexados"]!);
            Assert.Equal(0, (int)cuerpo1["documentosSinCambios"]!);

            // Puntos de 1024d en Qdrant (bge-m3 nativo, sin truncar).
            var puntos = await _qdrant.ScrollAsync(coleccion, limit: 10, payloadSelector: new WithPayloadSelector { Enable = false },
                vectorsSelector: new WithVectorsSelector { Enable = true });
            Assert.NotEmpty(puntos.Result);
            Assert.All(puntos.Result, p => Assert.Equal(1024, p.Vectors.Vectors.Vectors["dense"].Dense.Data.Count));

            using var busqueda = await client.PostJson($"/api/colecciones/{coleccion}/busquedas",
                new { consulta = "¿Para que sirve esta nota?", topK = 3 });
            Assert.Equal(HttpStatusCode.OK, busqueda.StatusCode);
            var resultados = (JArray)(await busqueda.Leer<JObject>())["resultados"]!;
            Assert.NotEmpty(resultados);

            using var segunda = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos = new[] { new { ruta = "docs/nota.md", contenido = DocumentoChico } } }
            });
            var cuerpo2 = await segunda.Leer<JObject>();
            Assert.Equal(0, (int)cuerpo2["documentosIndexados"]!);
            Assert.Equal(1, (int)cuerpo2["documentosSinCambios"]!);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereAltheiaFact]
    public async Task UnaColeccionDeOtraDimensionSeRechazaSinMezclarModelos()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(Token(sub: IdentidadDePrueba.SubAna, usuario: "ana"));
        try
        {
            var vectores = new VectorParamsMap();
            vectores.Map["dense"] = new VectorParams { Size = 256, Distance = Distance.Cosine };
            await _qdrant.CreateCollectionAsync(coleccion, vectorsConfig: vectores);

            using var response = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos = new[] { new { ruta = "docs/nota.md", contenido = DocumentoChico } } }
            });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("256", await response.Content.ReadAsStringAsync());
            Assert.Equal(0ul, await _qdrant.CountAsync(coleccion));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereAltheiaFact]
    public async Task HealthReadyComprueboAltheia()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(null);
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
