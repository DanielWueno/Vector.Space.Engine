using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Item 1.7.1: un cuerpo por encima del limite de la accion de ingestas responde 413, no 500. TestServer no aplica
/// MaxRequestBodySize (ver docs/evaluaciones/1.7.1 y la verificacion manual contra Kestrel real), asi que esta
/// prueba no reproduce el corte de Kestrel: confirma que <see cref="Reyma.VectorSpace.Engine.Microservice.Api.Filters.ReymaExceptionFilterAttribute"/>
/// traduce la <see cref="BadHttpRequestException"/> que Kestrel lanza en ese caso (StatusCode 413) a 413 con un
/// mensaje claro, igual que lo hace para las demas excepciones del switch.
/// </summary>
[Collection("Api")]
public sealed class CuerpoExcedidoTests(ApiFactory factory)
{
    [Fact]
    public async Task CuerpoPorEncimaDelLimiteDevuelve413ConMensajeClaro()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/cuerpo-excedido-prueba");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(413, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task UnaBadHttpRequestExceptionDeOtroStatusNoSeTraduceA413()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/cuerpo-excedido-prueba/otro-status");

        // Solo el caso de cuerpo excedido (413) tiene mapeo especifico; otro StatusCode cae al default (500),
        // igual que antes de 1.7.1.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}

[ApiController]
[Route("cuerpo-excedido-prueba")]
[AllowAnonymous]
public sealed class CuerpoExcedidoPruebaController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() =>
        throw new BadHttpRequestException(
            "Request body too large. The max request body size is 30000000 bytes.",
            StatusCodes.Status413PayloadTooLarge);

    [HttpGet("otro-status")]
    public IActionResult OtroStatus() =>
        throw new BadHttpRequestException("Malformed request.", StatusCodes.Status400BadRequest);
}
