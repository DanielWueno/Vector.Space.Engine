using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// 1.7.7 (resumen de negocio reanudable) contra Qdrant y TEI reales de <c>docker-compose.motor.yml</c>
/// (<c>RequiereMotorLocalFact</c>): el vector <c>dense-resumen</c> reusa el mismo perfil de embeddings por defecto
/// (mecanicamente equivalente a un perfil bge-m3 dedicado; lo que se verifica aqui es el mecanismo de Qdrant, no la
/// calidad de un modelo concreto). El LLM se sustituye por <see cref="LlmDePrueba"/>, registrado por DI
/// (<c>ConIdentidad</c>, parametro <c>servicios</c>): no hay ninguna llamada real a AltheIA en estas pruebas, asi que
/// no gastan tokens de la cuota real. La evidencia contra AltheIA real (modelo, enable_thinking, coste) vive en
/// <c>MotorAltheiaIntegracionTests</c>-style pruebas gateadas por separado (ver resultado de este item en el ledger).
/// </summary>
[Collection("Api")]
public sealed class ResumenIntegracionTests(ApiFactory factory)
{
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

    private const string DocSinNegocio = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        """;

    private WebApplicationFactory<Program> Api(ILlmResumenBrain llm, int topeTokens, params string[] colecciones)
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Qdrant:PuertoGrpc"] = "16334",
            ["Motor:Qdrant:PuertoRest"] = "16333",
            ["Motor:Embeddings:UrlBase"] = "http://127.0.0.1:18080",
            // Nunca se dispara de verdad: ILlmResumenBrain se sustituye por DI (ver 'servicios' abajo). Solo tiene
            // que pasar ValidateOnStart (URL http(s) absoluta).
            ["Motor:Llm:UrlBase"] = $"http://127.0.0.1:{ApiFactory.SinServicio}",
            ["Motor:Resumen:PerfilEmbeddings"] = "tei-arctic-256",
            ["Motor:Resumen:TopeTokensPorCorrida"] = topeTokens.ToString(),
            ["Motor:Resumen:TamanoPaginaPendientes"] = "64",
        };
        return factory.ConIdentidad(config, servicios: s => s.AddSingleton<ILlmResumenBrain>(llm));
    }

    private static string NuevaColeccion() => "it-resumen-" + Guid.NewGuid().ToString("N")[..10];

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    private static object CuerpoIngesta(string ruta, string contenido, bool? resumen = null) => resumen is null
        ? new { origen = new { tipo = "documentos", documentos = new[] { new { ruta, contenido } } } }
        : new { origen = new { tipo = "documentos", documentos = new[] { new { ruta, contenido } } }, resumen };

    /// <summary>LLM de prueba: cada llamada "cuesta" <see cref="TokensPorLlamada"/> tokens; nunca toca la red.</summary>
    private sealed class LlmDePrueba(int tokensPorLlamada = 300) : ILlmResumenBrain
    {
        private int _llamadas;

        public int Llamadas => Volatile.Read(ref _llamadas);
        public string ModelName => "llm-prueba";
        public string PromptVersion => "prompt-prueba-v1";

        public Task<ResumenLlmResultado?> ResumirAsync(string lenguaje, string rutaRelativa, string contenido, CancellationToken ct)
        {
            Interlocked.Increment(ref _llamadas);
            var sinNegocio = contenido.Contains("using System", StringComparison.Ordinal);
            var texto = sinNegocio ? LlmResumenBrain.Sentinel : $"Resumen de prueba de {rutaRelativa}.";
            return Task.FromResult<ResumenLlmResultado?>(new ResumenLlmResultado(texto, sinNegocio, tokensPorLlamada));
        }
    }

    /// <summary>Sidecar de chat simulado para el <see cref="LlmResumenBrain"/> real (1.26): cuenta llamadas, sin red.</summary>
    private sealed class SidecarLlm : HttpMessageHandler
    {
        private int _llamadas;

        public int Llamadas => Volatile.Read(ref _llamadas);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _llamadas);
            var json = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = "Pantalla de prueba: registra una accion de negocio." } } },
                usage = new { total_tokens = 300 }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>Mismo modelo, solo cambia <paramref name="maxTokens"/>: el caso que PromptVersion v1 no distinguia.</summary>
    private static LlmResumenBrain BrainReal(SidecarLlm sidecar, int maxTokens) =>
        new(new HttpClient(sidecar, disposeHandler: false) { BaseAddress = new Uri("http://sidecar-llm.invalid/") },
            new LlmOptions { UrlBase = "http://sidecar-llm.invalid", Modelo = "qwen3.8-27b-fast", MaxTokens = maxTokens, EnableThinking = null },
            NullLogger<LlmResumenBrain>.Instance);

    private async Task<Dictionary<string, string>> VersionesDePromptPorRuta(string coleccion)
    {
        var puntos = await _qdrant.ScrollAsync(coleccion, limit: 100,
            payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: new WithVectorsSelector { Enable = false });
        return puntos.Result.ToDictionary(
            p => p.Payload["relative_path"].StringValue,
            p => p.Payload.TryGetValue("resumen_prompt_version", out var v) ? v.StringValue : "");
    }

    [RequiereMotorLocalFact]
    public async Task CambiarMaxTokensDelMismoModeloNoRepiteLoYaResumidoYLoNuevoLlevaLaVersionNueva()
    {
        // 1.26: resumir con el perfil A, cambiar a B (mismo modelo, otro MaxTokens) y reingerir.
        var coleccion = NuevaColeccion();
        var sidecarA = new SidecarLlm();
        var sidecarB = new SidecarLlm();
        var brainA = BrainReal(sidecarA, maxTokens: 160);
        var brainB = BrainReal(sidecarB, maxTokens: 4096);
        Assert.Equal(brainA.ModelName, brainB.ModelName);
        Assert.NotEqual(brainA.PromptVersion, brainB.PromptVersion);
        try
        {
            using (var clienteA = Api(brainA, topeTokens: 100_000, coleccion).Cliente(TokenAna))
            {
                using var primera = await clienteA.PostJson($"/api/colecciones/{coleccion}/ingestas", CuerpoIngesta("a.md", DocA, resumen: true));
                Assert.True(primera.IsSuccessStatusCode, $"{(int)primera.StatusCode}: {await primera.Content.ReadAsStringAsync()}");
                Assert.Equal(1, (int)(await primera.Leer<JObject>())["resumenesGenerados"]!);
            }
            Assert.Equal(1, sidecarA.Llamadas);

            using var clienteB = Api(brainB, topeTokens: 100_000, coleccion).Cliente(TokenAna);
            using var segunda = await clienteB.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new
                {
                    tipo = "documentos",
                    documentos = new[] { new { ruta = "a.md", contenido = DocA }, new { ruta = "b.md", contenido = DocB } }
                },
                resumen = true
            });
            Assert.True(segunda.IsSuccessStatusCode, $"{(int)segunda.StatusCode}: {await segunda.Content.ReadAsStringAsync()}");
            var cuerpo = await segunda.Leer<JObject>();
            Assert.Equal(1, (int)cuerpo["documentosSinCambios"]!);
            Assert.Equal(1, (int)cuerpo["resumenesGenerados"]!);
            Assert.Equal(0, (int)cuerpo["resumenesPendientes"]!);

            // El chunk sin cambios no se vuelve a resumir (0 llamadas extra a A ni a B por el); solo el nuevo llama a B.
            Assert.Equal(1, sidecarA.Llamadas);
            Assert.Equal(1, sidecarB.Llamadas);

            var versiones = await VersionesDePromptPorRuta(coleccion);
            Assert.Equal(brainA.PromptVersion, versiones["a.md"]);
            Assert.Equal(brainB.PromptVersion, versiones["b.md"]);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task UnChunkExcluidoPorElFiltroPrevioNoLlamaAlLlmYQuedaProcesadoSinVectorNiTexto()
    {
        // 1.27: el .csproj lo excluye el filtro previo por defecto; la nota de negocio sigue yendo al LLM.
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;
        var coleccion = NuevaColeccion();
        var llm = new LlmDePrueba();
        using var client = Api(llm, topeTokens: 100_000, coleccion).Cliente(TokenAna);
        try
        {
            using var respuesta = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new
                {
                    tipo = "documentos",
                    documentos = new[] { new { ruta = "src/App/App.csproj", contenido = csproj }, new { ruta = "a.md", contenido = DocA } }
                },
                resumen = true
            });
            Assert.True(respuesta.IsSuccessStatusCode, $"{(int)respuesta.StatusCode}: {await respuesta.Content.ReadAsStringAsync()}");
            var cuerpo = await respuesta.Leer<JObject>();
            Assert.Equal(1, (int)cuerpo["resumenesGenerados"]!);
            Assert.Equal(1, (int)cuerpo["resumenesOmitidosPorFiltro"]!);
            Assert.Equal(0, (int)cuerpo["resumenesPendientes"]!);
            Assert.Equal(1, llm.Llamadas); // solo a.md; el .csproj no gasto tokens

            var puntos = await _qdrant.ScrollAsync(coleccion, limit: 10,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = true });
            var proyecto = Assert.Single(puntos.Result, p => p.Payload["relative_path"].StringValue.EndsWith(".csproj", StringComparison.Ordinal));
            Assert.False(proyecto.Payload["resumen_pending"].BoolValue);
            Assert.Equal("filtro-previo:v2", proyecto.Payload["resumen_modelo"].StringValue);
            Assert.Matches("^[0-9a-f]{8}$", proyecto.Payload["resumen_prompt_version"].StringValue);
            Assert.False(proyecto.Payload.ContainsKey("resumen_texto"));
            Assert.False(proyecto.Vectors.Vectors.Vectors.ContainsKey("dense-resumen"));

            var nota = Assert.Single(puntos.Result, p => p.Payload["relative_path"].StringValue == "a.md");
            Assert.Equal("llm-prueba", nota.Payload["resumen_modelo"].StringValue);
            Assert.True(nota.Vectors.Vectors.Vectors.ContainsKey("dense-resumen"));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task ColeccionCreadaConResumenGuardaDenseResumenEnLosChunksConContenidoDeNegocio()
    {
        var coleccion = NuevaColeccion();
        var llm = new LlmDePrueba();
        using var client = Api(llm, topeTokens: 100_000, coleccion).Cliente(TokenAna);
        try
        {
            using var respuesta = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", CuerpoIngesta("a.md", DocA, resumen: true));
            Assert.True(respuesta.IsSuccessStatusCode, $"{(int)respuesta.StatusCode}: {await respuesta.Content.ReadAsStringAsync()}");
            var cuerpo = await respuesta.Leer<JObject>();
            Assert.Equal(1, (int)cuerpo["resumenesGenerados"]!);
            Assert.Equal(0, (int)cuerpo["resumenesPendientes"]!);
            Assert.True((int)cuerpo["resumenTokensConsumidos"]! > 0);
            Assert.Equal(1, llm.Llamadas);

            var puntos = await _qdrant.ScrollAsync(coleccion, limit: 10,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = true });
            var punto = Assert.Single(puntos.Result);
            Assert.True(punto.Vectors.Vectors.Vectors.ContainsKey("dense-resumen"));
            Assert.Equal(256, punto.Vectors.Vectors.Vectors["dense-resumen"].Dense.Data.Count);
            Assert.False(punto.Payload["resumen_pending"].BoolValue);
            Assert.Equal("llm-prueba", punto.Payload["resumen_modelo"].StringValue);
            Assert.Equal("prompt-prueba-v1", punto.Payload["resumen_prompt_version"].StringValue);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task UnChunkSinContenidoDeNegocioNoLlevaVectorDeResumen()
    {
        var coleccion = NuevaColeccion();
        var llm = new LlmDePrueba();
        using var client = Api(llm, topeTokens: 100_000, coleccion).Cliente(TokenAna);
        try
        {
            using var respuesta = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", CuerpoIngesta("imports.cs", DocSinNegocio, resumen: true));
            Assert.True(respuesta.IsSuccessStatusCode, $"{(int)respuesta.StatusCode}: {await respuesta.Content.ReadAsStringAsync()}");

            var puntos = await _qdrant.ScrollAsync(coleccion, limit: 10,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = true });
            var punto = Assert.Single(puntos.Result);
            Assert.False(punto.Vectors.Vectors.Vectors.ContainsKey("dense-resumen"));
            Assert.False(punto.Payload["resumen_pending"].BoolValue);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task UnaColeccionSinResumenRechazaActivarloConMensajeClaroNo500()
    {
        var coleccion = NuevaColeccion();
        var llm = new LlmDePrueba();
        using var client = Api(llm, topeTokens: 100_000, coleccion).Cliente(TokenAna);
        try
        {
            using var primera = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", CuerpoIngesta("a.md", DocA));
            Assert.Equal(HttpStatusCode.OK, primera.StatusCode);

            using var activar = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", CuerpoIngesta("b.md", DocB, resumen: true));
            Assert.Equal(HttpStatusCode.Conflict, activar.StatusCode);
            var cuerpo = await activar.Content.ReadAsStringAsync();
            Assert.Contains("dense-resumen", cuerpo);
            Assert.Equal(0, llm.Llamadas);

            // No escribio nada por el intento rechazado: sigue solo el punto de la primera ingesta.
            Assert.Equal(1ul, await _qdrant.CountAsync(coleccion));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task DosChunksConElMismoContenidoSoloGastanUnaLlamadaAlLlm()
    {
        var coleccion = NuevaColeccion();
        var llm = new LlmDePrueba();
        using var client = Api(llm, topeTokens: 100_000, coleccion).Cliente(TokenAna);
        try
        {
            using var respuesta = await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
            {
                origen = new
                {
                    tipo = "documentos",
                    documentos = new[]
                    {
                        new { ruta = "a.md", contenido = DocA },
                        new { ruta = "copia-de-a.md", contenido = DocA } // mismo contenido -> mismo content_hash
                    }
                },
                resumen = true
            });
            Assert.True(respuesta.IsSuccessStatusCode, $"{(int)respuesta.StatusCode}: {await respuesta.Content.ReadAsStringAsync()}");
            var cuerpo = await respuesta.Leer<JObject>();
            Assert.Equal(2, (int)cuerpo["resumenesGenerados"]!);
            Assert.Equal(0, (int)cuerpo["resumenesPendientes"]!);

            // El segundo chunk reutilizo la cache (content_hash + version de prompt + modelo): una sola llamada real.
            Assert.Equal(1, llm.Llamadas);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereMotorLocalFact]
    public async Task UnaCorridaCortadaPorElTopeDeTokensSeReanudaSinRepetirLosYaResumidos()
    {
        var coleccion = NuevaColeccion();
        const int tokensPorLlamada = 300;
        var llm = new LlmDePrueba(tokensPorLlamada);
        // Tope para 2 llamadas por corrida: tras la 2a, el acumulado (600) ya iguala/supera el tope (500) y corta
        // antes de la 3a.
        using var client = Api(llm, topeTokens: 500, coleccion).Cliente(TokenAna);
        try
        {
            var documentos = Enumerable.Range(1, 5)
                .Select(i => new { ruta = $"doc-{i}.md", contenido = $"# Nota {i}\n\nPantalla numero {i}: hace algo de negocio distinto y verificable en la prueba." })
                .ToArray();

            using var primera = await client.PostJson($"/api/colecciones/{coleccion}/ingestas",
                new { origen = new { tipo = "documentos", documentos }, resumen = true });
            Assert.True(primera.IsSuccessStatusCode, $"{(int)primera.StatusCode}: {await primera.Content.ReadAsStringAsync()}");
            var cuerpo1 = await primera.Leer<JObject>();
            Assert.Equal(5, (int)cuerpo1["documentosIndexados"]!);
            Assert.Equal(2, (int)cuerpo1["resumenesGenerados"]!);
            Assert.Equal(3, (int)cuerpo1["resumenesPendientes"]!);
            Assert.Equal(2, llm.Llamadas);

            // Reingestar los MISMOS documentos (sin cambios): la fase 1 no hace nada nuevo, pero la fase 2 retoma los
            // 3 pendientes de la corrida anterior sin volver a resumir los 2 ya hechos.
            using var segunda = await client.PostJson($"/api/colecciones/{coleccion}/ingestas",
                new { origen = new { tipo = "documentos", documentos }, resumen = true });
            var cuerpo2 = await segunda.Leer<JObject>();
            Assert.Equal(0, (int)cuerpo2["documentosIndexados"]!);
            Assert.Equal(5, (int)cuerpo2["documentosSinCambios"]!);
            Assert.Equal(2, (int)cuerpo2["resumenesGenerados"]!); // 2 mas de los 3 pendientes
            Assert.Equal(1, (int)cuerpo2["resumenesPendientes"]!);
            Assert.Equal(4, llm.Llamadas); // 2 + 2, nunca repite los 2 de la primera corrida

            using var tercera = await client.PostJson($"/api/colecciones/{coleccion}/ingestas",
                new { origen = new { tipo = "documentos", documentos }, resumen = true });
            var cuerpo3 = await tercera.Leer<JObject>();
            Assert.Equal(1, (int)cuerpo3["resumenesGenerados"]!); // el ultimo pendiente
            Assert.Equal(0, (int)cuerpo3["resumenesPendientes"]!);
            Assert.Equal(5, llm.Llamadas); // total = numero de chunks distintos, ni uno repetido

            // Reingerir sin cambios, ya totalmente resumido: 0 tokens de LLM (criterio 3).
            using var cuarta = await client.PostJson($"/api/colecciones/{coleccion}/ingestas",
                new { origen = new { tipo = "documentos", documentos }, resumen = true });
            var cuerpo4 = await cuarta.Leer<JObject>();
            Assert.Equal(5, (int)cuerpo4["documentosSinCambios"]!);
            Assert.Equal(0, (int)cuerpo4["resumenesGenerados"]!);
            Assert.Equal(0, (int)cuerpo4["resumenesPendientes"]!);
            Assert.Equal(0, (int)cuerpo4["resumenTokensConsumidos"]!);
            Assert.Equal(5, llm.Llamadas); // sin cambios: ninguna llamada nueva
        }
        finally
        {
            await Borrar(coleccion);
        }
    }
}
