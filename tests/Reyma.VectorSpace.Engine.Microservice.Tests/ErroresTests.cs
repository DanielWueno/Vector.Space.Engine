using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

[Collection("Api")]
public sealed class ErroresTests(ApiFactory factory)
{
    [Fact]
    public async Task ErrorNoControladoDevuelve500ConTraceIdYSeRegistra()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/error-prueba");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        Assert.Equal(ErrorPruebaController.Message,
            Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray()).GetString());
        Assert.Contains(factory.Errors.Entries, entry => entry.Level == LogLevel.Error
            && entry.Exception is InvalidOperationException
            && entry.Message == ErrorPruebaController.Message);
    }
}

[ApiController]
[Route("error-prueba")]
[AllowAnonymous]
public sealed class ErrorPruebaController : ControllerBase
{
    public const string Message = "Fallo de prueba no controlado.";

    [HttpGet]
    public IActionResult Get() => throw new InvalidOperationException(Message);
}
