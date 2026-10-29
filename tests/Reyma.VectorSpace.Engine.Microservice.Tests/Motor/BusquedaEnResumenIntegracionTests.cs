using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Item 1.20: busqueda explicita sobre <c>dense-resumen</c> (<c>espacio: "resumen"</c>) contra Qdrant y TEI reales de
/// <c>docker-compose.motor.yml</c> (<c>RequiereMotorLocalFact</c>), con el LLM inyectado (0 tokens, mismo patron que
/// <see cref="ResumenIntegracionTests"/>). El resumen usa un perfil propio, <see cref="PerfilResumen"/> (mismo TEI a
/// 128d), distinto del perfil del codigo (256d): si la consulta se vectorizara con el perfil equivocado, la dimension
/// no coincidiria con <c>dense-resumen</c>. Colecciones <c>it-1-20-*</c>, borradas al terminar.
/// </summary>
[Collection("Api")]
public sealed class BusquedaEnResumenIntegracionTests(ApiFactory factory)
{
    private const string PerfilResumen = "it-resumen-128";
    private const string PerfilResumenOtro = "it-resumen-128-otro";
    private const string TeiVivo = "http://127.0.0.1:18080";
    private static readonly string SidecarCaido = $"http://127.0.0.1:{ApiFactory.SinServicio}";

    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");
    private readonly QdrantClient _qdrant = new("127.0.0.1", 16334);

    private const string DocA = """
        # Nota A

        Pantalla de inicio de sesion: valida usuario y contrasena contra el directorio corporativo antes de permitir el acceso.
        """;

    private const string DocB = """
        # Nota B

        Pantalla de recuperacion de contrasena: envia un enlace de un solo uso al correo registrado del usuario.
        """;

    private const string DocC = """
        # Nota C

        Reporte mensual de ventas: agrupa los pedidos facturados por sucursal y calcula el margen bruto de cada una.
        """;

    private const string DocD = """
        # Nota D

        Alta de proveedores: registra la razon social, el RFC y la cuenta bancaria antes de autorizar el primer pago.
        """;

    private const string DocSinNegocio = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        """;

    private const string Consulta = "pantalla para entrar al sistema con usuario y contrasena";

    /// <summary>LLM de prueba: nunca toca la red; el texto identifica la ruta resumida.</summary>
    private sealed class LlmDePrueba : ILlmResumenBrain
    {
        public string ModelName => "llm-prueba";
        public string PromptVersion => "prompt-prueba-v1";

        public static string TextoPara(string ruta) => $"Resumen de prueba de {ruta}.";

        public Task<ResumenLlmResultado?> ResumirAsync(string lenguaje, string rutaRelativa, string contenido, CancellationToken ct)
        {
            var sinNegocio = contenido.Contains("using System", StringComparison.Ordinal);
            var texto = sinNegocio ? LlmResumenBrain.Sentinel : TextoPara(rutaRelativa);
            return Task.FromResult<ResumenLlmResultado?>(new ResumenLlmResultado(texto, sinNegocio, 300));
        }
    }

    private WebApplicationFactory<Program> Api(
        string perfilResumen = PerfilResumen, string sidecar = TeiVivo, int topeTokens = 100_000,
        IDictionary<string, string?>? extra = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Qdrant:PuertoGrpc"] = "16334",
            ["Motor:Qdrant:PuertoRest"] = "16333",
            ["Motor:Embeddings:UrlBase"] = sidecar,
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:UrlBase"] = sidecar,
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:Modelo"] = "Snowflake/snowflake-arctic-embed-m-v2.0",
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:Dimension"] = "128",
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:PrefijoConsulta"] = "query: ",
            // Misma dimension, otro modelo declarado: reproduce el hueco que perfil_resumen cierra.
            [$"Motor:Embeddings:Perfiles:{PerfilResumenOtro}:UrlBase"] = sidecar,
            [$"Motor:Embeddings:Perfiles:{PerfilResumenOtro}:Modelo"] = "modelo-resumen-otro",
            [$"Motor:Embeddings:Perfiles:{PerfilResumenOtro}:Dimension"] = "128",
            ["Motor:Llm:UrlBase"] = SidecarCaido, // nunca se llama: ILlmResumenBrain se sustituye por DI
            ["Motor:Resumen:PerfilEmbeddings"] = perfilResumen,
            ["Motor:Resumen:TopeTokensPorCorrida"] = topeTokens.ToString(),
        };
        foreach (var (clave, valor) in extra ?? new Dictionary<string, string?>())
            config[clave] = valor;
        return factory.ConIdentidad(config, servicios: s => s.AddSingleton<ILlmResumenBrain>(new LlmDePrueba()));
    }

    private static string NuevaColeccion() => "it-1-20-" + Guid.NewGuid().ToString("N")[..8];

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    private static async Task Ingerir(HttpClient client, string coleccion, bool resumen, params (string Ruta, string Contenido)[] documentos)
    {
        using var response = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
        {
            origen = new { tipo = "documentos", documentos = documentos.Select(d => new { ruta = d.Ruta, contenido = d.Contenido }) },
            resumen
        });
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static Task<HttpResponseMessage> PostBusqueda(HttpClient client, string coleccion, string? espacio, int topK = 100) =>
        client.PostJson($"/api/colecciones/{coleccion}/busquedas", espacio is null
            ? new { consulta = Consulta, topK }
            : (object)new { consulta = Consulta, topK, espacio });

    private static async Task<JArray> Buscar(HttpClient client, string coleccion, string? espacio, int topK = 100)
    {
        using var response = await PostBusqueda(client, coleccion, espacio, topK);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (JArray)(await response.Leer<JObject>())["resultados"]!;
    }

    private async Task<IReadOnlyList<RetrievedPoint>> Puntos(string coleccion) =>
        (await _qdrant.ScrollAsync(coleccion, limit: 100,
            payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: new WithVectorsSelector { Enable = true })).Result;

    private static float[] Vector(RetrievedPoint punto, string nombre) =>
        punto.Vectors.Vectors.Vectors[nombre].Dense.Data.ToArray();

    private static float Coseno(float[] a, float[] b) => a.Zip(b, (x, y) => x * y).Sum(); // ambos normalizados L2

    /// <summary>Coleccion con resumen: A, B (negocio) y el chunk sin negocio resumidos; C o D queda pendiente.</summary>
    private async Task CrearColeccionConResumenYUnPendiente(string coleccion)
    {
        using (var client = Api().Cliente(TokenAna))
            await Ingerir(client, coleccion, resumen: true, ("a.md", DocA), ("b.md", DocB), ("imports.cs", DocSinNegocio));

        // Tope 1 token: la fase 2 resume un chunk (300 tokens) y corta, dejando el otro pendiente.
        using (var client = Api(topeTokens: 1).Cliente(TokenAna))
            await Ingerir(client, coleccion, resumen: true, ("c.md", DocC), ("d.md", DocD));
    }

    [RequiereMotorLocalFact]
    public async Task ElEspacioResumenSoloDevuelvePuntosConDenseResumenVectorizandoConElPerfilDeResumenYTraeElTexto()
    {
        var coleccion = NuevaColeccion();
        var api = Api();
        using var client = api.Cliente(TokenAna);
        try
        {
            await CrearColeccionConResumenYUnPendiente(coleccion);

            var puntos = await Puntos(coleccion);
            var conResumen = puntos.Where(p => p.Vectors.Vectors.Vectors.ContainsKey("dense-resumen")).ToList();
            var pendientes = puntos.Where(p => p.Payload["resumen_pending"].BoolValue).ToList();
            var sinNegocio = puntos.Single(p => p.Payload["relative_path"].StringValue == "imports.cs");
            Assert.Single(pendientes);
            Assert.False(sinNegocio.Vectors.Vectors.Vectors.ContainsKey("dense-resumen"));
            Assert.False(sinNegocio.Payload.ContainsKey("resumen_texto"));
            Assert.Equal(3, conResumen.Count); // a.md, b.md y el que si alcanzo a resumirse de c.md/d.md

            // La metadata guarda el perfil del resumen al crear la coleccion (cierra el hueco de modelo).
            using (var rest = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:16333/") })
            {
                using var info = JsonDocument.Parse(await rest.GetStringAsync($"collections/{coleccion}"));
                Assert.Equal(PerfilResumen,
                    info.RootElement.GetProperty("result").GetProperty("config").GetProperty("metadata").GetProperty("perfil_resumen").GetString());
            }

            var resultados = await Buscar(client, coleccion, "resumen");

            // Exactamente los puntos con dense-resumen (topK holgado y sin umbral por defecto): ni el pendiente ni el
            // "sin contenido de negocio" aparecen.
            Assert.Equal(
                conResumen.Select(p => p.Id.Uuid).OrderBy(x => x, StringComparer.Ordinal),
                resultados.Select(r => (string)r["id"]!).OrderBy(x => x, StringComparer.Ordinal));
            Assert.DoesNotContain(resultados, r => (string)r["id"]! == pendientes[0].Id.Uuid);

            // El hit trae el texto que genero el LLM inyectado, igual al que quedo en el payload.
            foreach (var r in resultados)
            {
                var ruta = (string)r["ruta"]!;
                Assert.Equal(LlmDePrueba.TextoPara(ruta), (string?)r["resumen"]);
                var punto = puntos.Single(p => p.Id.Uuid == (string)r["id"]!);
                Assert.Equal(LlmDePrueba.TextoPara(ruta), punto.Payload["resumen_texto"].StringValue);
            }

            // La consulta se vectorizo con el perfil de resumen y SU prefijo de consulta: la puntuacion de cada hit es
            // el coseno entre EmbedQueryAsync de ese perfil y el dense-resumen guardado; sin prefijo no coincide.
            var brain = api.Services.GetRequiredService<IVectorizationBrainFactory>().Resolver(PerfilResumen).Brain;
            var consulta = await brain.EmbedQueryAsync(Consulta, CancellationToken.None);
            var consultaSinPrefijo = (await brain.EmbedDocumentsAsync([Consulta], CancellationToken.None))[0];
            Assert.Equal(128, consulta.Length);
            foreach (var r in resultados)
            {
                var guardado = Vector(puntos.Single(p => p.Id.Uuid == (string)r["id"]!), "dense-resumen");
                Assert.Equal(Coseno(consulta, guardado), (float)r["puntuacion"]!, 3);
            }
            var top = Vector(puntos.Single(p => p.Id.Uuid == (string)resultados[0]["id"]!), "dense-resumen");
            Assert.True(Math.Abs(Coseno(consultaSinPrefijo, top) - (float)resultados[0]["puntuacion"]!) > 1e-3,
                "La puntuacion coincide tambien sin prefijo: la prueba no distingue si se uso el prefijo de consulta.");

            // En el espacio de codigo, el hit tambien trae el resumen persistido; null en el pendiente y el sin negocio.
            var codigo = await Buscar(client, coleccion, "codigo");
            foreach (var r in codigo)
            {
                var id = (string)r["id"]!;
                var esperado = conResumen.Any(p => p.Id.Uuid == id) ? LlmDePrueba.TextoPara((string)r["ruta"]!) : null;
                Assert.Equal(esperado, (string?)r["resumen"]);
            }
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task EspacioCodigoEsElDeSiempreYEnFusionNativaLaRamaResumenSigueSiendoSoloDensa()
    {
        var coleccion = NuevaColeccion();
        var api = Api();
        using var client = api.Cliente(TokenAna);
        try
        {
            await CrearColeccionConResumenYUnPendiente(coleccion);
            var puntos = await Puntos(coleccion);

            // (1) Ausente y "codigo": mismos ids, orden y puntuaciones; y la puntuacion es el coseno contra 'dense' con
            //     el perfil por defecto (el espacio de siempre), no contra dense-resumen.
            var sinEspacio = await Buscar(client, coleccion, espacio: null);
            var codigo = await Buscar(client, coleccion, "codigo");
            Assert.NotEmpty(sinEspacio);
            Assert.Equal(sinEspacio.Select(r => (string)r["id"]!), codigo.Select(r => (string)r["id"]!));
            Assert.Equal(sinEspacio.Select(r => (float)r["puntuacion"]!), codigo.Select(r => (float)r["puntuacion"]!));
            var brainCodigo = api.Services.GetRequiredService<IVectorizationBrainFactory>().Resolver(null).Brain;
            var consultaCodigo = await brainCodigo.EmbedQueryAsync(Consulta, CancellationToken.None);
            foreach (var r in sinEspacio)
                Assert.Equal(Coseno(consultaCodigo, Vector(puntos.Single(p => p.Id.Uuid == (string)r["id"]!), "dense")), (float)r["puntuacion"]!, 3);
            // El conjunto devuelto es exactamente el de los puntos con coseno >= 0,10 (UmbralSimilitud) contra 'dense',
            // recalculado aparte: tener o no dense-resumen no cambia nada en el espacio de codigo.
            var esperados = puntos
                .Where(p => Coseno(consultaCodigo, Vector(p, "dense")) >= 0.10f)
                .Select(p => p.Id.Uuid).OrderBy(x => x, StringComparer.Ordinal);
            Assert.Equal(esperados, sinEspacio.Select(r => (string)r["id"]!).OrderBy(x => x, StringComparer.Ordinal));

            // (5) FusionNativa: la rama resumen da exactamente lo mismo que en Denso (cosenos, no RRF).
            var densa = await Buscar(client, coleccion, "resumen");
            using var fusion = Api(extra: new Dictionary<string, string?> { ["Motor:Busqueda:Modo"] = "FusionNativa" }).Cliente(TokenAna);
            var enFusion = await Buscar(fusion, coleccion, "resumen");
            Assert.Equal(densa.Select(r => (string)r["id"]!), enFusion.Select(r => (string)r["id"]!));
            Assert.Equal(densa.Select(r => (float)r["puntuacion"]!), enFusion.Select(r => (float)r["puntuacion"]!));

            // (7) Umbral: por defecto no se aplica (vuelven todos los puntos con dense-resumen); configurado, si.
            var minimo = densa.Min(r => (float)r["puntuacion"]!);
            var maximo = densa.Max(r => (float)r["puntuacion"]!);
            Assert.True(minimo < maximo);
            var umbral = (minimo + maximo) / 2;
            using var conUmbral = Api(extra: new Dictionary<string, string?>
            {
                ["Motor:Busqueda:UmbralSimilitudResumen"] = umbral.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }).Cliente(TokenAna);
            var filtrados = await Buscar(conUmbral, coleccion, "resumen");
            Assert.NotEmpty(filtrados);
            Assert.True(filtrados.Count < densa.Count);
            Assert.All(filtrados, r => Assert.True((float)r["puntuacion"]! >= umbral));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task EsquemaIncompatibleResponde409AntesDeLlamarAlSidecarYColeccionInexistente200()
    {
        var sinResumen = NuevaColeccion();
        var conResumen = NuevaColeccion();
        try
        {
            using (var client = Api().Cliente(TokenAna))
            {
                await Ingerir(client, sinResumen, resumen: false, ("a.md", DocA));
                await Ingerir(client, conResumen, resumen: true, ("a.md", DocA));
            }

            // Instancias con el sidecar caido: si alguna llegara a vectorizar responderia 503, no 409. El control
            // (espacio de codigo con esa misma instancia) confirma que el sidecar si esta caido.
            using var mismoPerfil = Api(sidecar: SidecarCaido).Cliente(TokenAna);
            using (var control = await PostBusqueda(mismoPerfil, sinResumen, "codigo"))
                Assert.Equal(HttpStatusCode.ServiceUnavailable, control.StatusCode);

            // Sin dense-resumen en el esquema real de Qdrant.
            using (var respuesta = await PostBusqueda(mismoPerfil, sinResumen, "resumen"))
            {
                Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
                Assert.Contains("dense-resumen", await respuesta.Content.ReadAsStringAsync());
            }

            // dense-resumen de 128d contra el perfil por defecto (256d) como perfil de resumen de la instancia.
            using (var otraDimension = Api(perfilResumen: "tei-arctic-256", sidecar: SidecarCaido).Cliente(TokenAna))
            using (var respuesta = await PostBusqueda(otraDimension, conResumen, "resumen"))
            {
                Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
                Assert.Contains("dimension 128", await respuesta.Content.ReadAsStringAsync());
            }

            // Misma dimension (128) pero otro perfil de resumen: lo detecta perfil_resumen de la metadata.
            using (var otroPerfil = Api(perfilResumen: PerfilResumenOtro, sidecar: SidecarCaido).Cliente(TokenAna))
            using (var respuesta = await PostBusqueda(otroPerfil, conResumen, "resumen"))
            {
                Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
                Assert.Contains(PerfilResumen, await respuesta.Content.ReadAsStringAsync());
            }

            // Coleccion inexistente: 200 sin resultados, igual que en el espacio de codigo.
            using (var respuesta = await PostBusqueda(mismoPerfil, NuevaColeccion(), "resumen"))
            {
                Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
                Assert.Empty((JArray)(await respuesta.Leer<JObject>())["resultados"]!);
            }

            // Y con el perfil correcto y el sidecar vivo, la misma coleccion si responde.
            using var vivo = Api().Cliente(TokenAna);
            Assert.NotEmpty(await Buscar(vivo, conResumen, "resumen"));
        }
        finally
        {
            await Borrar(sinResumen, conResumen);
        }
    }
}
