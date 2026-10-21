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
    public async Task ReadinessReportaCaidasQdrantYElSidecarDeEmbeddings(string path)
    {
        // Item 1.4: el readiness comprueba las dependencias reales del motor. ApiFactory las apunta a un puerto sin
        // servicio: no puede responder sano, y debe hacerlo pronto (cada check tiene 5 s de limite).
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await client.GetAsync(path, timeout.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SelfNoDependeDeQdrantNiDelSidecar()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/self");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
