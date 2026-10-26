using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// 1.7.7 contra AltheIA real: perfil de LLM <c>qwen3.8-27b-fast</c> (1.7.3: 30/30 resumenes, nunca vacio) y perfil de
/// embeddings <c>bge-m3</c> a 1024d (sin truncar) para <c>dense-resumen</c>. Solo con <c>VSE_ALTHEIA_LOCAL=1</c>,
/// <c>ALTHEIA_KEY</c>/<c>ALTHEIA_CERT</c> (variables de usuario) y docker-compose.motor.yml para Qdrant (16334) y TEI
/// (perfil de embeddings POR DEFECTO, arctic local, gratis: solo el vector dense-resumen y el LLM hablan con AltheIA).
/// Documentos minimos a proposito (igual criterio que <see cref="MotorAltheiaIntegracionTests"/>): el tope de tokens
/// de esta clase de pruebas se fija bajo (ver <c>ToleranciaTokens</c>) para demostrar el corte/reanudacion con pocos
/// chunks reales, gastando una fraccion minima de la cuota diaria compartida (confirmado por el usuario: tope duro de
/// 200 000 tokens de LLM para TODA la verificacion de este item; estas pruebas gastan del orden de unos pocos miles).
/// </summary>
[Collection("Api")]
public sealed class ResumenAltheiaIntegracionTests(ApiFactory factory)
{
    private const string PerfilResumenEmbeddings = "altheia-bge-m3-1024";
    private readonly QdrantClient _qdrant = new("127.0.0.1", 16334);

    private WebApplicationFactory<Program> Api(int topeTokens, params string[] colecciones)
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Qdrant:PuertoGrpc"] = "16334",
            ["Motor:Qdrant:PuertoRest"] = "16333",
            // Perfil de embeddings POR DEFECTO: TEI local (arctic), gratis -- sirve el vector 'dense' principal.
            ["Motor:Embeddings:UrlBase"] = "http://127.0.0.1:18080",
            // Perfil adicional AltheIA/bge-m3 a 1024d SIN truncar (1.7.3), dedicado al vector dense-resumen.
            [$"Motor:Embeddings:Perfiles:{PerfilResumenEmbeddings}:UrlBase"] = "https://altheia",
            [$"Motor:Embeddings:Perfiles:{PerfilResumenEmbeddings}:Ruta"] = "v1/embeddings",
            [$"Motor:Embeddings:Perfiles:{PerfilResumenEmbeddings}:Modelo"] = "bge-m3",
            [$"Motor:Embeddings:Perfiles:{PerfilResumenEmbeddings}:Dimension"] = "1024",
            [$"Motor:Embeddings:Perfiles:{PerfilResumenEmbeddings}:PrefijoConsulta"] = "",
            [$"Motor:Embeddings:Perfiles:{PerfilResumenEmbeddings}:ApiKey"] = Environment.GetEnvironmentVariable("ALTHEIA_KEY"),
            [$"Motor:Embeddings:Perfiles:{PerfilResumenEmbeddings}:RutaCertificadoCA"] = Environment.GetEnvironmentVariable("ALTHEIA_CERT"),
            // Perfil de LLM: AltheIA real, qwen3.8-27b-fast (1.7.3: responde sin razonar dentro de MaxTokens).
            ["Motor:Llm:UrlBase"] = "https://altheia",
            ["Motor:Llm:Ruta"] = "v1/chat/completions",
            ["Motor:Llm:Modelo"] = "qwen3.8-27b-fast",
            ["Motor:Llm:MaxTokens"] = "160",
            ["Motor:Llm:ApiKey"] = Environment.GetEnvironmentVariable("ALTHEIA_KEY"),
            ["Motor:Llm:RutaCertificadoCA"] = Environment.GetEnvironmentVariable("ALTHEIA_CERT"),
            ["Motor:Resumen:PerfilEmbeddings"] = PerfilResumenEmbeddings,
            ["Motor:Resumen:TopeTokensPorCorrida"] = topeTokens.ToString(),
            ["Motor:Resumen:TamanoPaginaPendientes"] = "64",
        };
        return factory.ConIdentidad(config);
    }

    private static string NuevaColeccion() => "it-resumen-altheia-" + Guid.NewGuid().ToString("N")[..10];

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    /// <summary>
    /// Evidencia real de los 4 puntos del criterio, con el minimo de chunks necesario: documento minimo real (no
    /// "sin contenido de negocio") por chunk, tope de tokens bajo a proposito para forzar el corte a los 2 primeros
    /// de 3.
    /// </summary>
    [RequiereAltheiaFact]
    public async Task ResumenDeNegocioReanudableContraAltheiaReal()
    {
        var coleccion = NuevaColeccion();
        // 1.7.3: ~670 tokens por chunk. Tope de 1000 corta tras el primero (670 < 1000 sigue; el segundo ya pasaria
        // de 1000, se corta ANTES de pedirlo): demuestra el corte con el minimo posible de llamadas reales.
        using var client = Api(topeTokens: 1000, coleccion).Cliente(Token(sub: SubAna, usuario: "ana"));
        try
        {
            var documentos = new[]
            {
                new { ruta = "login.cs", contenido = "public class LoginController { public bool Autenticar(string usuario, string clave) => Directorio.Validar(usuario, clave); }" },
                new { ruta = "logout.cs", contenido = "public class LogoutController { public void CerrarSesion(string tokenSesion) => Sesiones.Revocar(tokenSesion); }" },
                new { ruta = "recuperar.cs", contenido = "public class RecuperarClaveController { public void EnviarEnlace(string correo) => Correo.Enviar(correo, Enlaces.DeUnSoloUso()); }" },
            };

            // --- (1) y (4) parcial: primera corrida, corte por tope de tokens ---
            using var primera = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos },
                resumen = true
            });
            Assert.True(primera.IsSuccessStatusCode, $"{(int)primera.StatusCode}: {await primera.Content.ReadAsStringAsync()}");
            var cuerpo1 = await primera.Leer<JObject>();
            var generados1 = (int)cuerpo1["resumenesGenerados"]!;
            var pendientes1 = (int)cuerpo1["resumenesPendientes"]!;
            var tokens1 = (int)cuerpo1["resumenTokensConsumidos"]!;
            Assert.True(generados1 >= 1 && generados1 < 3, $"Se esperaba un corte parcial (1 o 2 de 3); generados={generados1}");
            Assert.True(pendientes1 > 0, "El tope de tokens deberia haber dejado pendientes para reanudar.");
            Assert.True(tokens1 > 0);

            // Criterio 1: la coleccion creada con resumen guarda dense-resumen en los chunks con contenido de negocio.
            var puntos = await _qdrant.ScrollAsync(coleccion, limit: 10,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = true });
            Assert.Equal(3, puntos.Result.Count);
            var yaResumidos = puntos.Result.Where(p => !p.Payload["resumen_pending"].BoolValue).ToList();
            Assert.Equal(generados1, yaResumidos.Count);
            Assert.All(yaResumidos, p =>
            {
                Assert.True(p.Vectors.Vectors.Vectors.ContainsKey("dense-resumen"));
                Assert.Equal(1024, p.Vectors.Vectors.Vectors["dense-resumen"].Dense.Data.Count);
                Assert.Equal("qwen3.8-27b-fast", p.Payload["resumen_modelo"].StringValue);
            });

            // --- Criterio 2: la corrida se reanuda sin repetir los ya resumidos ---
            using var segunda = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos },
                resumen = true
            });
            var cuerpo2 = await segunda.Leer<JObject>();
            Assert.Equal(3, (int)cuerpo2["documentosSinCambios"]!); // fase 1: nada cambio
            var generados2 = (int)cuerpo2["resumenesGenerados"]!;
            var pendientes2 = (int)cuerpo2["resumenesPendientes"]!;
            Assert.Equal(0, pendientes2); // con tope 1000 alcanza para el resto (maximo 2 restantes, ~1340 tokens)
            Assert.Equal(3, generados1 + generados2); // total = 3 chunks, ninguno repetido

            // --- Criterio 3: reingerir sin cambios, ya totalmente resumido, gasta 0 tokens de LLM ---
            using var tercera = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos },
                resumen = true
            });
            var cuerpo3 = await tercera.Leer<JObject>();
            Assert.Equal(3, (int)cuerpo3["documentosSinCambios"]!);
            Assert.Equal(0, (int)cuerpo3["resumenesGenerados"]!);
            Assert.Equal(0, (int)cuerpo3["resumenesPendientes"]!);
            Assert.Equal(0, (int)cuerpo3["resumenTokensConsumidos"]!);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    /// <summary>Criterio 4: una coleccion sin resumen rechaza activarlo con un mensaje claro (409, nunca 500). Sin costo de LLM.</summary>
    [RequiereAltheiaFact]
    public async Task UnaColeccionSinResumenRechazaActivarloContraAltheiaReal()
    {
        var coleccion = NuevaColeccion();
        using var client = Api(topeTokens: 100_000, coleccion).Cliente(Token(sub: SubAna, usuario: "ana"));
        try
        {
            using var primera = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos = new[] { new { ruta = "a.cs", contenido = "public class A { public void M() {} }" } } }
            });
            Assert.Equal(HttpStatusCode.OK, primera.StatusCode);

            using var activar = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new { tipo = "documentos", documentos = new[] { new { ruta = "b.cs", contenido = "public class B { public void M() {} }" } } },
                resumen = true
            });
            Assert.Equal(HttpStatusCode.Conflict, activar.StatusCode);
            Assert.Contains("dense-resumen", await activar.Content.ReadAsStringAsync());
        }
        finally
        {
            await Borrar(coleccion);
        }
    }
}
