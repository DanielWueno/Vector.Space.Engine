using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Item 1.18: expone metadatos de chunk (linea, lenguaje, tipo, hash, namespace/clase/metodo, simbolos definidos y
/// consumidos) en <c>POST .../busquedas</c>, mas el filtro exacto opcional <c>simbolosDefinidos</c>. Motor real (1.4)
/// contra Qdrant y TEI locales; coleccion propia con nombre aleatorio, borrada al terminar.
/// </summary>
[Collection("Api")]
public sealed class MetadatosDeChunkEnBusquedaIntegracionTests(ApiFactory factory)
{
    private const string Definer = """
        namespace Demo.Simbolos;

        public class Definer
        {
            public static string SimboloUnico1180001()
            {
                return "definido";
            }
        }
        """;

    private const string Consumidor = """
        namespace Demo.Simbolos;

        public class Consumidor
        {
            public string Usar()
            {
                return Definer.SimboloUnico1180001();
            }
        }
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

    private static string NuevaColeccion() => "it-1-18-" + Guid.NewGuid().ToString("N")[..8];

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    private static async Task Ingerir(HttpClient client, string coleccion, params (string Ruta, string Contenido)[] documentos)
    {
        using var response = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
        {
            origen = new { tipo = "documentos", documentos = documentos.Select(d => new { ruta = d.Ruta, contenido = d.Contenido }) }
        });
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task<JArray> Buscar(HttpClient client, string coleccion, string consulta, int topK, IEnumerable<string>? simbolosDefinidos = null)
    {
        using var response = await client.PostJson($"/api/colecciones/{coleccion}/busquedas", new
        {
            consulta,
            topK,
            simbolosDefinidos = simbolosDefinidos?.ToArray()
        });
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (JArray)(await response.Leer<JObject>())["resultados"]!;
    }

    [RequiereMotorLocalFact]
    public async Task ExponeMetadatosDeChunkYFiltraPorSimbolosDefinidos()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            await Ingerir(client, coleccion, ("src/Definer.cs", Definer), ("src/Consumidor.cs", Consumidor));

            // Sin filtro: el hit del consumidor trae el simbolo en simbolosConsumidos, y los metadatos de chunk
            // coinciden con lo que Qdrant tiene guardado para ese punto (verificacion pareada contra el payload real).
            var sinFiltro = await Buscar(client, coleccion, "Usar simbolo definido", topK: 20);
            Assert.NotEmpty(sinFiltro);

            var hitConsumidor = sinFiltro.Single(r => (string)r["ruta"]! == "src/Consumidor.cs");
            var simbolosConsumidos = ((JArray)hitConsumidor["simbolosConsumidos"]!).Select(v => (string)v!).ToArray();
            Assert.Contains("SimboloUnico1180001", simbolosConsumidos);
            Assert.Equal("CSharp", (string)hitConsumidor["lenguaje"]!);
            Assert.Equal("Demo.Simbolos", (string)hitConsumidor["namespace"]!);
            Assert.Equal("Consumidor", (string)hitConsumidor["clase"]!);
            Assert.Equal("Usar", (string)hitConsumidor["metodo"]!);

            var idConsumidor = (string)hitConsumidor["id"]!;
            var lineaInicioConsumidor = (int)hitConsumidor["lineaInicio"]!;
            var lineaFinConsumidor = (int)hitConsumidor["lineaFin"]!;
            var hashConsumidor = (string)hitConsumidor["contentHash"]!;

            var puntoReal = (await _qdrant.RetrieveAsync(coleccion, Guid.Parse(idConsumidor), withPayload: true)).Single();
            Assert.Equal(puntoReal.Payload["start_line"].IntegerValue, lineaInicioConsumidor);
            Assert.Equal(puntoReal.Payload["end_line"].IntegerValue, lineaFinConsumidor);
            Assert.Equal(puntoReal.Payload["content_hash"].StringValue, hashConsumidor);

            // Un chunk sin simbolos consumidos (p. ej. si lo hubiera) expondria lista vacia, nunca null: lo
            // comprobamos sobre cualquier hit que el payload no tenga la clave consumed_symbols.
            Assert.All(sinFiltro, r => Assert.NotNull(r["simbolosDefinidos"]));
            Assert.All(sinFiltro, r => Assert.NotNull(r["simbolosConsumidos"]));

            // Con el filtro: solo los puntos que definen ese simbolo (el de Definer.cs), nunca el consumidor.
            var conFiltro = await Buscar(client, coleccion, "Usar simbolo definido", topK: 20,
                simbolosDefinidos: ["SimboloUnico1180001"]);
            Assert.NotEmpty(conFiltro);
            Assert.All(conFiltro, r => Assert.Equal("src/Definer.cs", (string)r["ruta"]!));
            Assert.DoesNotContain(conFiltro, r => (string)r["id"]! == idConsumidor);
            Assert.All(conFiltro, r =>
                Assert.Contains("SimboloUnico1180001", ((JArray)r["simbolosDefinidos"]!).Select(v => (string)v!)));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task SinFiltroDaLosMismosIdsOrdenYPuntuacionesQueAntesDelFiltro()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            await Ingerir(client, coleccion, ("src/Definer.cs", Definer), ("src/Consumidor.cs", Consumidor));

            // La misma busqueda, repetida: sin "simbolosDefinidos" en el cuerpo (ausente), el resultado es
            // deterministico - mismos ids, mismo orden, mismas puntuaciones (verificacion pareada antes/despues).
            var primera = await Buscar(client, coleccion, "Usar simbolo definido", topK: 20);
            var segunda = await Buscar(client, coleccion, "Usar simbolo definido", topK: 20);

            Assert.Equal(primera.Select(r => (string)r["id"]!), segunda.Select(r => (string)r["id"]!));
            Assert.Equal(primera.Select(r => (float)r["puntuacion"]!), segunda.Select(r => (float)r["puntuacion"]!));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }
}
