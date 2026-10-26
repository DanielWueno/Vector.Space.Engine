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
            options ?? new LlmOptions { UrlBase = "http://sidecar-llm.invalid", Modelo = "qwen3.8-27b-fast", MaxTokens = 160 },
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
    public void PromptVersionCambiaSiCambiaElModelo()
    {
        var a = LlmResumenBrain.ComputePromptVersion("qwen3.8-27b-fast");
        var b = LlmResumenBrain.ComputePromptVersion("qwen3.8-27b-deep");
        var aOtraVez = LlmResumenBrain.ComputePromptVersion("qwen3.8-27b-fast");

        Assert.NotEqual(a, b);
        Assert.Equal(a, aOtraVez);
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
