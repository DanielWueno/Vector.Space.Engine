using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>Solo con <c>VSE_MOTOR_LOCAL=1</c> y docker-compose.motor.yml levantado (Qdrant 16334, TEI 18080).</summary>
public sealed class RequiereMotorLocalFactAttribute : FactAttribute
{
    public const string Variable = "VSE_MOTOR_LOCAL";

    public RequiereMotorLocalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"Requiere {Variable}=1 y docker-compose.motor.yml levantado (Qdrant 127.0.0.1:16334, TEI 127.0.0.1:18080).";
    }
}

/// <summary>Igual que <see cref="RequiereMotorLocalFactAttribute"/> pero para <c>[Theory]</c> (1.7.5).</summary>
public sealed class RequiereMotorLocalTheoryAttribute : TheoryAttribute
{
    public RequiereMotorLocalTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable(RequiereMotorLocalFactAttribute.Variable) != "1")
            Skip = $"Requiere {RequiereMotorLocalFactAttribute.Variable}=1 y docker-compose.motor.yml levantado " +
                   "(Qdrant 127.0.0.1:16334, TEI 127.0.0.1:18080).";
    }
}

/// <summary>
/// Motor real (1.4) por el contrato HTTP contra el Qdrant y el TEI locales: los escenarios de riesgo de una ingesta
/// incremental por documento. Cada prueba usa colecciones propias con nombre aleatorio y las borra al terminar.
/// Puertos fijos de docker-compose.motor.yml: nunca rag-qdrant (6334).
/// </summary>
[Collection("Api")]
public sealed class MotorIntegracionTests(ApiFactory factory)
{
    private const string Clase = """
        namespace Demo.Pedidos;

        public class ServicioPedidos
        {
            public decimal CalcularTotal(IEnumerable<decimal> lineas)
            {
                var total = 0m;
                foreach (var linea in lineas) total += linea;
                return total;
            }

            public bool PuedeCancelarse(DateTime creado, DateTime ahora)
            {
                return (ahora - creado).TotalHours < 24 && creado <= ahora;
            }
        }
        """;

    private const string Guia = """
        # Guia de pedidos

        ## Cancelacion

        Un pedido se puede cancelar durante las primeras veinticuatro horas desde su creacion, sin costo para el cliente.

        ## Facturacion

        La factura se emite al cerrar el pedido y se envia por correo electronico al cliente registrado en la cuenta.
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

    private static string NuevaColeccion() => "it-" + Guid.NewGuid().ToString("N")[..10];

    private static async Task<JObject> Ingerir(HttpClient client, string coleccion, params (string Ruta, string Contenido)[] documentos)
    {
        using var response = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
        {
            origen = new { tipo = "documentos", documentos = documentos.Select(d => new { ruta = d.Ruta, contenido = d.Contenido }) }
        });
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return JObject.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed record Punto(string Ruta, string Hash, long Linea);

    private async Task<Dictionary<string, Punto>> Puntos(string coleccion)
    {
        var puntos = new Dictionary<string, Punto>();
        var r = await _qdrant.ScrollAsync(coleccion, limit: 1000, payloadSelector: new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector { Fields = { "relative_path", "content_hash", "start_line" } }
        });
        foreach (var p in r.Result)
            puntos[p.Id.Uuid] = new Punto(p.Payload["relative_path"].StringValue, p.Payload["content_hash"].StringValue,
                p.Payload["start_line"].IntegerValue);
        return puntos;
    }

    // La coleccion es el repositorio de la clave de identidad: el mismo documento tiene otros Ids en otra coleccion.
    // Entre colecciones se compara lo que el chunk contiene y donde empieza.
    private static IEnumerable<Punto> Contenido(Dictionary<string, Punto> puntos) =>
        puntos.Values.OrderBy(p => p.Ruta, StringComparer.Ordinal).ThenBy(p => p.Linea).ThenBy(p => p.Hash, StringComparer.Ordinal);

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    private static void Conteos(JObject r, int indexados, int sinCambios, int omitidos = 0)
    {
        Assert.Equal(indexados, (int)r["documentosIndexados"]!);
        Assert.Equal(sinCambios, (int)r["documentosSinCambios"]!);
        Assert.Equal(omitidos, (int)r["documentosOmitidos"]!);
    }

    [RequiereMotorLocalFact]
    public async Task LaIngestaIncrementalPorDocumentoDejaLosMismosPuntosQueUnaDesdeCero()
    {
        string coleccion = NuevaColeccion(), limpia = NuevaColeccion();
        using var client = Api(coleccion, limpia).Cliente(TokenAna);
        try
        {
            Conteos(await Ingerir(client, coleccion, ("src/ServicioPedidos.cs", Clase), ("docs/guia.md", Guia)), indexados: 2, sinCambios: 0);
            var inicial = await Puntos(coleccion);
            Assert.Contains(inicial.Values, p => p.Ruta == "src/ServicioPedidos.cs");
            Assert.Contains(inicial.Values, p => p.Ruta == "docs/guia.md");

            // Lo mismo otra vez, y el mismo contenido con CRLF: nada cambia ni se reescribe.
            Conteos(await Ingerir(client, coleccion, ("src/ServicioPedidos.cs", Clase), ("docs/guia.md", Guia)), indexados: 0, sinCambios: 2);
            Conteos(await Ingerir(client, coleccion, ("src/ServicioPedidos.cs", Clase.Replace("\n", "\r\n"))), indexados: 0, sinCambios: 1);
            Assert.Equal(inicial, await Puntos(coleccion));

            // Un metodo cambia: solo ese documento se reprocesa y su chunk viejo desaparece.
            var modificada = Clase.Replace("< 24", "< 48");
            Conteos(await Ingerir(client, coleccion, ("src/ServicioPedidos.cs", modificada), ("docs/guia.md", Guia)), indexados: 1, sinCambios: 1);
            var incremental = await Puntos(coleccion);
            Conteos(await Ingerir(client, limpia, ("src/ServicioPedidos.cs", modificada), ("docs/guia.md", Guia)), indexados: 2, sinCambios: 0);
            Assert.Equal(Contenido(await Puntos(limpia)), Contenido(incremental));
            Assert.NotEqual(inicial.Keys.Order(), incremental.Keys.Order());
            Assert.Equal(inicial.Count, incremental.Count);

            // Un documento que ya no genera chunks se queda sin puntos; el otro no se toca.
            Conteos(await Ingerir(client, coleccion, ("src/ServicioPedidos.cs", "")), indexados: 1, sinCambios: 0);
            var final = await Puntos(coleccion);
            Assert.All(final.Values, p => Assert.Equal("docs/guia.md", p.Ruta));
            Assert.Equal(incremental.Where(p => p.Value.Ruta == "docs/guia.md"), final);
        }
        finally
        {
            await Borrar(coleccion, limpia);
        }
    }

    [RequiereMotorLocalFact]
    public async Task BusquedaYEmbeddingsUsanLosMismosVectoresQueLaIngesta()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            // Coleccion autorizada pero aun sin ingestar: sin resultados, no error.
            using (var vacia = await client.PostJson($"/api/colecciones/{coleccion}/busquedas", new { consulta = "pedido" }))
            {
                Assert.Equal(HttpStatusCode.OK, vacia.StatusCode);
                Assert.Empty((JArray)(await vacia.Leer<JObject>())["resultados"]!);
            }

            await Ingerir(client, coleccion, ("src/ServicioPedidos.cs", Clase), ("docs/guia.md", Guia));

            using var busqueda = await client.PostJson($"/api/colecciones/{coleccion}/busquedas",
                new { consulta = "¿Cuánto tiempo hay para cancelar un pedido?", topK = 3 });
            var resultados = (JArray)(await busqueda.Leer<JObject>())["resultados"]!;
            Assert.NotEmpty(resultados);
            Assert.Contains("ancel", (string)resultados[0]["texto"]!);
            var puntuaciones = resultados.Select(r => (float)r["puntuacion"]!).ToList();
            Assert.Equal(puntuaciones.OrderDescending(), puntuaciones);

            // El vector de documento del endpoint de embeddings es el que la ingesta guardo para ese texto enriquecido.
            var id = (string)resultados[0]["id"]!;
            var punto = (await _qdrant.RetrieveAsync(coleccion, Guid.Parse(id), withPayload: true, withVectors: true)).Single();
            var guardado = punto.Vectors.Vectors.Vectors["dense"].Dense.Data.ToArray();
            using var embeddings = await client.PostJson($"/api/colecciones/{coleccion}/embeddings",
                new { textos = new[] { punto.Payload["enriched_content"].StringValue } });
            var cuerpo = await embeddings.Leer<JObject>();
            Assert.Equal("Snowflake/snowflake-arctic-embed-m-v2.0", (string)cuerpo["modelo"]!);
            Assert.Equal(256, (int)cuerpo["dimension"]!);
            var calculado = cuerpo["vectores"]![0]!.Select(v => (float)v).ToArray();
            Assert.Equal(256, calculado.Length);
            Assert.Equal(1.0, Math.Sqrt(calculado.Sum(v => (double)v * v)), 4);
            Assert.True(guardado.Zip(calculado, (a, b) => (double)a * b).Sum() > 0.9999);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task UnaColeccionConOtraDimensionSeRechazaSinTocarla()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            var vectores = new VectorParamsMap();
            vectores.Map["dense"] = new VectorParams { Size = 384, Distance = Distance.Cosine };
            await _qdrant.CreateCollectionAsync(coleccion, vectorsConfig: vectores);

            using var response = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos = new[] { new { ruta = "src/ServicioPedidos.cs", contenido = Clase } } }
            });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("384", await response.Content.ReadAsStringAsync());
            Assert.Equal(0ul, await _qdrant.CountAsync(coleccion));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task RutasQueSonElMismoDocumentoSeRechazanYLoFueraDelPerfilSeOmite()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(coleccion).Cliente(TokenAna);
        try
        {
            using (var duplicadas = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos = new[] { new { ruta = "src/A.cs", contenido = Clase }, new { ruta = "src\\A.cs", contenido = Clase } } }
            }))
                Assert.Equal(HttpStatusCode.BadRequest, duplicadas.StatusCode);

            Conteos(await Ingerir(client, coleccion, ("tools/script.py", "print('hola mundo desde un script que el perfil no admite')"), ("docs/guia.md", Guia)),
                indexados: 1, sinCambios: 0, omitidos: 1);
            Assert.All((await Puntos(coleccion)).Values, p => Assert.Equal("docs/guia.md", p.Ruta));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }
}
