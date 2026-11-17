using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Catalogo;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Catalogo de perfiles (GET api/catalogo/*) contra servidores simulados: lista lo mismo que la configuracion con la
/// que se validan las solicitudes, y el sondeo en vivo distingue disponible / modelo no ofrecido / credencial
/// rechazada / caido / lento sin exponer el host ni gastar tokens (solo GET).
/// </summary>
public sealed class CatalogoDelMotorTests
{
    private const string DeepConfigurado = "qwen3.8-27b-deep";

    private sealed class Servidor(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public Servidor(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : this((r, _) => responder(r))
        {
        }

        public List<(HttpMethod Metodo, string Ruta)> Peticiones { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Peticiones)
                Peticiones.Add((request.Method, request.RequestUri!.AbsolutePath));
            return responder(request, cancellationToken);
        }
    }

    private sealed class Fabrica(Dictionary<string, (Servidor Servidor, string Base)> clientes) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(clientes[name].Servidor, disposeHandler: false) { BaseAddress = new Uri(clientes[name].Base) };
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    /// <summary>TEI: sin /v1/models (404), con /info.</summary>
    private static Servidor Tei(string modelId = "Snowflake/snowflake-arctic-embed-m-v2.0") => new(r => r.RequestUri!.AbsolutePath switch
    {
        "/info" => Json(HttpStatusCode.OK, $$"""{"model_id":"{{modelId}}","max_input_length":8192}"""),
        _ => Json(HttpStatusCode.NotFound, "{}")
    });

    /// <summary>AltheIA (vLLM): /v1/models con los modelos habilitados para la clave.</summary>
    private static Servidor Altheia(params string[] modelos) => new(r => r.RequestUri!.AbsolutePath == "/v1/models"
        ? Json(HttpStatusCode.OK, "{\"data\":[" + string.Join(",", modelos.Select(m => $$"""{"id":"{{m}}","max_model_len":117964}""")) + "]}")
        : Json(HttpStatusCode.NotFound, "{}"));

    private static MotorOptions Opciones(bool resumen = true) => new()
    {
        Embeddings = new EmbeddingsOptions
        {
            UrlBase = "http://tei.interno.invalid",
            Perfiles = { ["altheia-bge-m3-1024"] = new PerfilEmbeddingsOptions { UrlBase = "https://altheia.interno.invalid", Modelo = "bge-m3", Dimension = 1024 } }
        },
        Llm = new LlmOptions
        {
            UrlBase = "https://altheia.interno.invalid",
            Perfiles = { ["altheia-qwen-fast"] = new PerfilLlmOptions { Modelo = "qwen3.8-27b-fast" } }
        },
        Resumen = new ResumenOptions { PerfilEmbeddings = resumen ? "altheia-bge-m3-1024" : null },
        Git = new GitOptions
        {
            OrigenesPermitidos =
            {
                new OrigenGitPermitidoOptions { Host = "dev.azure.com", PrefijoRuta = "/Organizacion/" },
                new OrigenGitPermitidoOptions { Esquema = "http", Host = "git.local.invalid", Puerto = 8080 }
            }
        }
    };

    private static CatalogoDelMotor Catalogo(
        MotorOptions opciones, Servidor? tei = null, Servidor? altheiaEmbeddings = null, Servidor? altheiaLlm = null, TimeSpan? plazo = null)
    {
        var llm = altheiaLlm ?? Altheia(DeepConfigurado, "qwen3.8-27b-fast");
        var fabrica = new Fabrica(new()
        {
            [TeiVectorizationBrain.HttpClientName] = (tei ?? Tei(), "http://tei.interno.invalid/"),
            [VectorizationBrainFactory.NombreCliente("altheia-bge-m3-1024")] = (altheiaEmbeddings ?? Altheia("bge-m3"), "https://altheia.interno.invalid/"),
            [LlmResumenBrain.HttpClientName] = (llm, "https://altheia.interno.invalid/"),
            [LlmResumenBrainFactory.NombreCliente("altheia-qwen-fast")] = (llm, "https://altheia.interno.invalid/"),
        });
        var o = Options.Create(opciones);
        var llmFactory = new LlmResumenBrainFactory(fabrica, o, NullLoggerFactory.Instance, new LlmSintetico(new ContadorSintetico()));
        return new CatalogoDelMotor(fabrica, o, llmFactory) { PlazoSonda = plazo ?? TimeSpan.FromSeconds(5) };
    }

    // --- Perfiles de embeddings ---

    [Fact]
    public async Task ListaCadaPerfilDeEmbeddingsDeclaradoConSuModeloVerificadoEnVivo()
    {
        var perfiles = await Catalogo(Opciones()).ListarPerfilesEmbeddingsAsync(CancellationToken.None);

        Assert.Collection(perfiles,
            porDefecto =>
            {
                Assert.Equal(MotorOptions.PerfilEmbeddingsPorDefecto, porDefecto.Nombre);
                Assert.Equal(256, porDefecto.Dimension);
                Assert.True(porDefecto.EsPorDefecto);
                Assert.False(porDefecto.UsadoPorResumen);
                Assert.True(porDefecto.Disponible);
                Assert.True(porDefecto.ModeloVerificado); // por /info de TEI
                Assert.Equal(8192, porDefecto.VentanaTokens);
                Assert.Null(porDefecto.Detalle);
            },
            altheia =>
            {
                Assert.Equal("altheia-bge-m3-1024", altheia.Nombre);
                Assert.Equal("bge-m3", altheia.Modelo);
                Assert.Equal(1024, altheia.Dimension);
                Assert.False(altheia.EsPorDefecto);
                Assert.True(altheia.UsadoPorResumen);
                Assert.True(altheia.Disponible);
                Assert.True(altheia.ModeloVerificado); // por /v1/models
            });
    }

    [Fact]
    public async Task UnModeloQueElServidorNoOfreceSaleNoDisponibleConLosQueSiOfrece()
    {
        var perfiles = await Catalogo(Opciones(), altheiaEmbeddings: Altheia("otro-modelo")).ListarPerfilesEmbeddingsAsync(CancellationToken.None);

        var altheia = perfiles.Single(p => p.Nombre == "altheia-bge-m3-1024");
        Assert.False(altheia.Disponible);
        Assert.Contains("bge-m3", altheia.Detalle);
        Assert.Contains("otro-modelo", altheia.Detalle);
    }

    [Fact]
    public async Task TeiQueSirveOtroModeloRespondePeroNoQuedaVerificado()
    {
        var perfiles = await Catalogo(Opciones(), tei: Tei("BAAI/bge-small")).ListarPerfilesEmbeddingsAsync(CancellationToken.None);

        var porDefecto = perfiles[0];
        Assert.True(porDefecto.Disponible);
        Assert.False(porDefecto.ModeloVerificado);
        Assert.Contains("BAAI/bge-small", porDefecto.Detalle);
    }

    [Theory]
    [InlineData("credencial", "credencial")]
    [InlineData("caido", "no responde")]
    [InlineData("todo-503", "503")]
    public async Task UnServidorConProblemasSaleNoDisponibleSinExponerElHost(string caso, string motivo)
    {
        var servidor = caso switch
        {
            "credencial" => new Servidor(_ => Json(HttpStatusCode.Unauthorized, "{}")),
            "caido" => new Servidor(_ => throw new HttpRequestException("Connection refused (altheia.interno.invalid:443)")),
            _ => new Servidor(_ => Json(HttpStatusCode.ServiceUnavailable, "{}")),
        };

        var perfiles = await Catalogo(Opciones(), altheiaEmbeddings: servidor).ListarPerfilesEmbeddingsAsync(CancellationToken.None);

        var altheia = perfiles.Single(p => p.Nombre == "altheia-bge-m3-1024");
        Assert.False(altheia.Disponible);
        Assert.Contains(motivo, altheia.Detalle);
        Assert.DoesNotContain("interno.invalid", altheia.Detalle);
        Assert.True(perfiles[0].Disponible); // un servidor caido no tumba el resto del catalogo
    }

    [Fact]
    public async Task UnServidorLentoNoRetieneElCatalogoMasQueElPlazoDeSondeo()
    {
        var lento = new Servidor(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        var perfiles = await Catalogo(Opciones(), altheiaEmbeddings: lento, plazo: TimeSpan.FromMilliseconds(200))
            .ListarPerfilesEmbeddingsAsync(CancellationToken.None);

        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Contains("no respondio", perfiles.Single(p => p.Nombre == "altheia-bge-m3-1024").Detalle);
    }

    [Fact]
    public async Task ElSondeoSoloHaceGetNuncaPideEmbeddingsNiCompletions()
    {
        var tei = Tei();
        var llm = Altheia(DeepConfigurado, "qwen3.8-27b-fast");
        var catalogo = Catalogo(Opciones(), tei: tei, altheiaLlm: llm);

        await catalogo.ListarPerfilesEmbeddingsAsync(CancellationToken.None);
        await catalogo.ListarPerfilesLlmAsync(CancellationToken.None);

        Assert.All(tei.Peticiones.Concat(llm.Peticiones), p => Assert.Equal(HttpMethod.Get, p.Metodo));
        Assert.DoesNotContain(llm.Peticiones, p => p.Ruta.Contains("completions"));
    }

    // --- Perfiles de LLM ---

    [Fact]
    public async Task ListaElPerfilDeLlmPorDefectoDeepPrimeroYLosAdicionalesHeredados()
    {
        var perfiles = await Catalogo(Opciones()).ListarPerfilesLlmAsync(CancellationToken.None);

        Assert.Collection(perfiles,
            deep =>
            {
                Assert.Equal("altheia-qwen-deep", deep.Nombre);
                Assert.Equal(DeepConfigurado, deep.Modelo);
                Assert.True(deep.Razonamiento);
                Assert.True(deep.EsPorDefecto);
                Assert.True(deep.Disponible);
                Assert.True(deep.ModeloVerificado);
                Assert.Equal(117964, deep.VentanaTokens);
            },
            fast =>
            {
                Assert.Equal("altheia-qwen-fast", fast.Nombre);
                Assert.Equal("qwen3.8-27b-fast", fast.Modelo);
                Assert.Equal(4096, fast.MaxTokens); // heredado del por defecto (fast para solo, ~40 tokens)
                Assert.Null(fast.Razonamiento);
                Assert.False(fast.EsPorDefecto);
                Assert.True(fast.Disponible);
            });
    }

    [Fact]
    public async Task SinResumenHabilitadoLosPerfilesDeLlmSalenNoDisponiblesSinLlamarAlServidor()
    {
        var llm = Altheia(DeepConfigurado);
        var perfiles = await Catalogo(Opciones(resumen: false), altheiaLlm: llm).ListarPerfilesLlmAsync(CancellationToken.None);

        Assert.All(perfiles, p =>
        {
            Assert.False(p.Disponible);
            Assert.Contains("no esta habilitado", p.Detalle);
        });
        Assert.Empty(llm.Peticiones);
    }

    // --- Validacion de perfilLlm (sin red) ---

    [Theory]
    [InlineData(null)]
    [InlineData("altheia-qwen-deep")]
    [InlineData("altheia-qwen-fast")]
    public void UnPerfilLlmRegistradoOAusenteEsValido(string? perfilLlm) =>
        Catalogo(Opciones()).ValidarPerfilLlm(perfilLlm);

    [Fact]
    public void UnPerfilLlmNoRegistradoEs400ConLosDisponibles()
    {
        var ex = Assert.Throws<BadRequestException>(() => Catalogo(Opciones()).ValidarPerfilLlm("no-existe"));

        Assert.Contains("altheia-qwen-deep", ex.Message);
        Assert.Contains("altheia-qwen-fast", ex.Message);
    }

    [Fact]
    public void PedirPerfilLlmSinResumenHabilitadoEs400()
    {
        var ex = Assert.Throws<BadRequestException>(() => Catalogo(Opciones(resumen: false)).ValidarPerfilLlm("altheia-qwen-fast"));

        Assert.Contains("no esta habilitado", ex.Message);
    }

    // --- Origenes Git ---

    [Fact]
    public void ListaLosOrigenesGitConUnEjemploDeUrlAceptada()
    {
        var origenes = Catalogo(Opciones()).ListarOrigenesGit();

        Assert.Collection(origenes,
            azure => Assert.Equal("https://dev.azure.com/Organizacion/<ruta-del-repositorio>", azure.Ejemplo),
            local => Assert.Equal("http://git.local.invalid:8080/<ruta-del-repositorio>", local.Ejemplo));
    }

    // --- Perfiles de ingesta: catalogo y validacion salen de la misma lista ---

    [Fact]
    public void CadaPerfilDeIngestaDelCatalogoEsAceptadoPorLaValidacion()
    {
        Assert.NotEmpty(PerfilDeIngesta.Catalogo);
        foreach (var perfil in PerfilDeIngesta.Catalogo)
            Assert.Empty(PerfilDeIngesta.Errores(perfil.Nombre, perfil.RequiereRepositorioLogico ? "repo" : null));
        Assert.NotEmpty(PerfilDeIngesta.Errores("no-existe", "repo"));
    }

    [Theory]
    [InlineData("v1/embeddings", "v1/models")]
    [InlineData("v1/chat/completions", "v1/models")]
    [InlineData("/v1/embeddings", "v1/models")]
    [InlineData("embeddings", "models")]
    public void LaRutaDeModelosSaleDeLaRutaDelPerfil(string ruta, string esperada) =>
        Assert.Equal(esperada, SondaDeServidor.RutaDeModelos(ruta));
}
