using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Cliente HTTP del perfil de LLM (1.7.7) contra un sidecar simulado: mismo patron que
/// <see cref="TeiVectorizationBrainTests"/> (sidecar fake con <see cref="HttpMessageHandler"/>, sin red real).
/// </summary>
public sealed class LlmResumenBrainTests
{
    private sealed class Sidecar(Func<JsonElement, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<JsonElement> Peticiones { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            Peticiones.Add(body);
            return responder(body);
        }
    }

    private static LlmResumenBrain Brain(HttpMessageHandler handler, LlmOptions? options = null) =>
        new(new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://sidecar-llm.invalid/") },
            options ?? new LlmOptions { UrlBase = "http://sidecar-llm.invalid", Modelo = "qwen3.8-27b-fast", MaxTokens = 160, EnableThinking = null },
            NullLogger<LlmResumenBrain>.Instance);

    private static HttpResponseMessage RespuestaConTexto(string texto, int totalTokens = 670) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = texto } } },
            usage = new { total_tokens = totalTokens }
        }), Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task EnviaElPromptDeSistemaYElContextoDelChunkComoUserPrompt()
    {
        var sidecar = new Sidecar(_ => RespuestaConTexto("Pantalla de login: valida usuario y contrasena."));
        var brain = Brain(sidecar);

        var resultado = await brain.ResumirAsync("CSharp", "src/Login.cs", "public class Login { }", CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.False(resultado!.SinContenidoDeNegocio);
        Assert.Equal(670, resultado.TokensConsumidos);

        var cuerpo = sidecar.Peticiones[0];
        Assert.Equal("qwen3.8-27b-fast", cuerpo.GetProperty("model").GetString());
        var mensajes = cuerpo.GetProperty("messages");
        Assert.Equal("system", mensajes[0].GetProperty("role").GetString());
        Assert.Equal("user", mensajes[1].GetProperty("role").GetString());
        Assert.Contains("SIN_CONTENIDO_DE_NEGOCIO", mensajes[0].GetProperty("content").GetString());
        Assert.Contains("Lenguaje: CSharp, Ruta: src/Login.cs", mensajes[1].GetProperty("content").GetString());
        Assert.Contains("public class Login", mensajes[1].GetProperty("content").GetString());
        Assert.False(cuerpo.TryGetProperty("chat_template_kwargs", out _));
    }

    [Fact]
    public async Task UnTextoQueEmpiezaConElCentinelaEsSinContenidoDeNegocio()
    {
        var brain = Brain(new Sidecar(_ => RespuestaConTexto("SIN_CONTENIDO_DE_NEGOCIO")));

        var resultado = await brain.ResumirAsync("CSharp", "src/Program.cs", "using System;", CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.True(resultado!.SinContenidoDeNegocio);
    }

    [Fact]
    public async Task UnaRespuestaVaciaEsNullNoUnaExcepcion()
    {
        // 1.7.3 midio esto con qwen3.8-27b-deep SIN enable_thinking=false: razona y gasta los tokens sin escribir nada.
        var brain = Brain(new Sidecar(_ => RespuestaConTexto("", totalTokens: 160)));

        var resultado = await brain.ResumirAsync("CSharp", "src/Program.cs", "using System;", CancellationToken.None);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task SoloEnviaChatTemplateKwargsCuandoEnableThinkingEstaConfigurado()
    {
        var sidecar = new Sidecar(_ => RespuestaConTexto("Resumen."));
        var opciones = new LlmOptions { UrlBase = "http://sidecar-llm.invalid", Modelo = "qwen3.8-27b-deep", MaxTokens = 160, EnableThinking = false };
        var brain = Brain(sidecar, opciones);

        await brain.ResumirAsync("CSharp", "a.cs", "class A {}", CancellationToken.None);

        var kwargs = sidecar.Peticiones[0].GetProperty("chat_template_kwargs");
        Assert.False(kwargs.GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public async Task ElPerfilPorDefectoPideDeepConRazonamientoYTopeHolgado()
    {
        // Sin tocar nada (2026-11-17): deep con enable_thinking=true y max_tokens 4096. Con pensamiento y 160 tokens deep
        // responde vacio (1.7.3, 0/30); con 4096, 100/100 sin cortes (docs/evaluaciones/resumen-fast-vs-deep-filtrado).
        var sidecar = new Sidecar(_ => RespuestaConTexto("Resumen."));
        var brain = Brain(sidecar, new LlmOptions { UrlBase = "http://sidecar-llm.invalid" });

        await brain.ResumirAsync("CSharp", "a.cs", "class A {}", CancellationToken.None);

        var cuerpo = sidecar.Peticiones[0];
        Assert.Equal("qwen3.8-27b-deep", cuerpo.GetProperty("model").GetString());
        Assert.True(cuerpo.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.Equal(4096, cuerpo.GetProperty("max_tokens").GetInt32());
    }

    private static string Version(string modelo = "qwen3.8-27b-fast", bool? enableThinking = null, int maxTokens = 160,
        double temperatura = 0.15, double topP = 0.95) =>
        LlmResumenBrain.ComputePromptVersion(new PerfilLlmEfectivo("p", null, "v1/chat/completions", modelo, maxTokens,
            temperatura, topP, enableThinking, 120, null, null));

    [Fact]
    public void PromptVersionCambiaSiCambiaElModelo()
    {
        Assert.NotEqual(Version(modelo: "qwen3.8-27b-fast"), Version(modelo: "qwen3.8-27b-deep"));
        Assert.Equal(Version(modelo: "qwen3.8-27b-fast"), Version(modelo: "qwen3.8-27b-fast"));
    }

    [Fact]
    public void PromptVersionDistingueDosPerfilesDelMismoModeloConDistintoMaxTokens()
    {
        // 1.26: el caso que v1 no distinguia (mismo modelo, otra configuracion de muestreo).
        Assert.NotEqual(Version(maxTokens: 160), Version(maxTokens: 4096));
    }

    [Fact]
    public void PromptVersionDistingueLosTresValoresDeEnableThinking()
    {
        // null no envia chat_template_kwargs: es otra peticion que false (1.7.3, experimento del 2026-11-11).
        var versiones = new[] { Version(enableThinking: null), Version(enableThinking: false), Version(enableThinking: true) };
        Assert.Equal(3, versiones.Distinct().Count());
    }

    [Fact]
    public void PromptVersionDistingueTemperaturaYTopP()
    {
        Assert.NotEqual(Version(temperatura: 0.15), Version(temperatura: 0.2));
        Assert.NotEqual(Version(topP: 0.95), Version(topP: 0.9));
    }

    [Fact]
    public void DosPerfilesConLaMismaConfiguracionDanLaMismaVersionAunqueDifieranEnNombreServidorOPlazo()
    {
        var porDefecto = new LlmOptions { Modelo = "qwen3.8-27b-fast", EnableThinking = null, UrlBase = "http://a.invalid" };
        var adicional = new PerfilLlmEfectivo("altheia-qwen-fast", "http://b.invalid", "v1/chat/completions",
            "qwen3.8-27b-fast", porDefecto.MaxTokens, porDefecto.Temperatura, porDefecto.TopP, null, 30, null, null);

        Assert.Equal(LlmResumenBrain.ComputePromptVersion(porDefecto), LlmResumenBrain.ComputePromptVersion(adicional));
    }

    [Fact]
    public void PromptVersionNoDependeDeLaCulturaDelServidor()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariante = Version(temperatura: 0.15, topP: 0.95);
            CultureInfo.CurrentCulture = new CultureInfo("es-MX"); // escribiria "0,15"
            Assert.Equal(invariante, Version(temperatura: 0.15, topP: 0.95));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void PromptVersionDelPerfilPorDefectoQuedaFijadoEnUnGolden()
    {
        // Golden calculado FUERA de este codigo (PowerShell, SHA-256 de
        // "v2|qwen3.8-27b-deep|true|4096|0.15|0.95|" + el literal del prompt con LF, 8 primeros hex): si cambia sin
        // querer el prompt, el formato del hash o un default de LlmOptions, esta prueba falla. Si el cambio es
        // deliberado, actualizar el golden y declararlo (cambia resumen_prompt_version de los chunks nuevos).
        // 2026-11-17: el por defecto paso de deep sin pensamiento a 160 (bf05a271) a deep con pensamiento a 4096.
        Assert.Equal("dac52c3f", LlmResumenBrain.ComputePromptVersion(new LlmOptions()));
        Assert.Equal("bf05a271", Version(modelo: "qwen3.8-27b-deep", enableThinking: false));
        Assert.Equal("60a4efa1", Version(modelo: "qwen3.8-27b-fast", enableThinking: null));
    }

    [Fact]
    public async Task ElCerebroExponeLaVersionDeSuPerfilYEnviaElPromptConLf()
    {
        var sidecar = new Sidecar(_ => RespuestaConTexto("Resumen."));
        var opciones = new LlmOptions { UrlBase = "http://sidecar-llm.invalid", Modelo = "qwen3.8-27b-fast", MaxTokens = 200, EnableThinking = null };
        var brain = Brain(sidecar, opciones);

        await brain.ResumirAsync("CSharp", "a.cs", "class A {}", CancellationToken.None);

        Assert.Equal(LlmResumenBrain.ComputePromptVersion(opciones), brain.PromptVersion);
        Assert.NotEqual(Version(maxTokens: 160), brain.PromptVersion);
        var system = sidecar.Peticiones[0].GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.DoesNotContain('\r', system);
    }

    public static TheoryData<HttpStatusCode, string> RespuestasDeModeloNoDisponible() => new()
    {
        { HttpStatusCode.Forbidden, """{"error":{"message":"API key 'clave-x' is not allowed to use model 'no-existe'.","type":"invalid_request_error","code":"model_not_allowed"}}""" },
        { HttpStatusCode.BadRequest, """{"error":{"message":"Model 'bge-m3' does not support chat.","type":"invalid_request_error","code":"model_capability_mismatch"}}""" },
    };

    [Theory]
    [MemberData(nameof(RespuestasDeModeloNoDisponible))]
    public async Task UnModeloQueAltheiaNoPermiteEsBadRequestNunca503(HttpStatusCode status, string cuerpo)
    {
        var brain = Brain(new Sidecar(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
        }));

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => brain.ResumirAsync("CSharp", "a.cs", "class A {}", CancellationToken.None));
        Assert.DoesNotContain("503", ex.Message);
    }

    [Fact]
    public async Task UnErrorDelSidecarEsMotorNoDisponibleSinLaUrlNiElDetalle()
    {
        var brain = Brain(new Sidecar(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("detalle interno de http://sidecar-llm.invalid")
        }));

        var ex = await Assert.ThrowsAsync<MotorNoDisponibleException>(
            () => brain.ResumirAsync("CSharp", "a.cs", "class A {}", CancellationToken.None));
        Assert.Contains("500", ex.Message);
        Assert.DoesNotContain("sidecar-llm.invalid", ex.Message);
    }
}
