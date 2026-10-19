using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Reyma.VectorSpace.Engine.Microservice.Business;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

[Collection("Api")]
public sealed class SeguridadTests(ApiFactory factory)
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task SwaggerNoSeExponeFueraDeDevelopment(string environment)
    {
        var configured = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = configured.CreateClient();

        foreach (var path in new[] { "/index.html", "/index.js", "/swagger/v1/swagger.json", "/oauth2-redirect.html" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://identity.invalid")]
    public async Task SwaggerEnDevelopmentUsaPkceYClientesConfiguradosSinValidadorExterno(string authority)
    {
        var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseConnection:Host"] = "database.invalid",
                    ["DatabaseConnection:Database"] = "test-db",
                    ["DatabaseConnection:UserId"] = "test-user",
                    ["DatabaseConnection:Password"] = Guid.NewGuid().ToString(),
                    ["IdentityServerAuthenticationOptions:Authority"] = authority,
                    ["SwaggerOptions:ClientId"] = "test-public-client",
                    ["SwaggerOptions:ClientSecret"] = "test-secret-value"
                }));
        });
        using var client = configured.CreateClient();
        var html = await client.GetStringAsync("/index.html");
        var script = await client.GetStringAsync("/index.js");

        Assert.Contains("index.js", html);
        Assert.Contains("\"usePkceWithAuthorizationCodeGrant\":true", script);
        foreach (var scope in new[] { "permissions.service.read", "integrity.service.read", "integrity.service.write" })
            Assert.Contains(scope, script);
        Assert.Contains("\"clientId\":\"test-public-client\"", script);
        Assert.Contains("\"clientSecret\":\"test-secret-value\"", script);
        Assert.DoesNotContain("validator.swagger.io", script);

        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var components = document.RootElement.GetProperty("components");
        if (authority.Length == 0)
        {
            Assert.False(components.TryGetProperty("securitySchemes", out var schemes)
                && schemes.TryGetProperty("oauth2", out _));
        }
        else
        {
            var flow = components.GetProperty("securitySchemes").GetProperty("oauth2")
                .GetProperty("flows").GetProperty("authorizationCode");
            Assert.Equal(authority + "/connect/authorize", flow.GetProperty("authorizationUrl").GetString());
            Assert.Equal(authority + "/connect/token", flow.GetProperty("tokenUrl").GetString());
            foreach (var scope in new[] { "permissions.service.read", "integrity.service.read", "integrity.service.write" })
                Assert.True(flow.GetProperty("scopes").TryGetProperty(scope, out _));
        }
    }

    [Fact]
    public void JwtValidaEmisorAudienciaVigenciaYExigeMetadatosHttps()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.True(options.RequireHttpsMetadata);
        Assert.Equal(ServicioInfo.NombreApi, options.Audience);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
        Assert.True(options.TokenValidationParameters.RequireExpirationTime);
    }

    [Fact]
    public async Task CorsVacioNoAutorizaOrigenExterno()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/self");
        request.Headers.Add("Origin", "https://untrusted.invalid");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
