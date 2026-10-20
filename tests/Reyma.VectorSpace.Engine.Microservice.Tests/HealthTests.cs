using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Reyma.VectorSpace.Engine.Microservice.Business;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

[Collection("Api")]
public sealed class HealthTests(ApiFactory factory)
{
    [Fact]
    public async Task SelfResponde200SinBaseDeDatos()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/self");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task ReadinessRespondeSanoSinDependenciasExternasReales(string path)
    {
        // Item 1.9: el servicio no tiene Postgres ni ninguna otra dependencia externa real todavia
        // (Qdrant/sidecar llega en 1.4, bloqueado); el readiness no debe fingir una dependencia que
        // no existe.
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(path, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SelfRespondeConYSinRutaBase()
    {
        var withBasePath = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ASPNETCORE_BASEPATH"] = ServicioInfo.RutaBase
                })));
        using var client = withBasePath.CreateClient();

        foreach (var path in new[] { "/health/self", ServicioInfo.RutaBase + "/health/self" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
