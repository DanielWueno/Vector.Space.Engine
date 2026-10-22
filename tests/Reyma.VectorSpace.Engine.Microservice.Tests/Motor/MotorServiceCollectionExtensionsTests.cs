using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Composicion del motor (1.13): el perfil AltheIA agrega clave Bearer opcional y CA adicional opcional al
/// <c>HttpClient</c> nombrado del sidecar de embeddings, sin tocar el perfil TEI local (sin clave, sin CA).
/// </summary>
public sealed class MotorServiceCollectionExtensionsTests
{
    private static ServiceProvider Construir(Dictionary<string, string?> overrides)
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Embeddings:UrlBase"] = "http://sidecar.invalid",
        };
        foreach (var (clave, valor) in overrides)
            config[clave] = valor;

        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMotorVectorial(configuracion);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ConClaveConfiguradaElClienteDelSidecarEnviaAuthorizationBearer()
    {
        using var sp = Construir(new() { ["Motor:Embeddings:ApiKey"] = "clave-de-prueba-altheia" });

        var cliente = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TeiVectorizationBrain.HttpClientName);

        Assert.NotNull(cliente.DefaultRequestHeaders.Authorization);
        Assert.Equal("Bearer", cliente.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("clave-de-prueba-altheia", cliente.DefaultRequestHeaders.Authorization!.Parameter);
    }

    [Fact]
    public void SinClaveConfiguradaElClienteDelSidecarNoEnviaAuthorization()
    {
        using var sp = Construir(new());

        var cliente = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TeiVectorizationBrain.HttpClientName);

        Assert.Null(cliente.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void ConRutaCertificadoCAInexistenteElArranqueFalla()
    {
        using var sp = Construir(new() { ["Motor:Embeddings:RutaCertificadoCA"] = @"C:\ruta\que\no\existe\ca.crt" });

        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<MotorOptions>>().Value);
    }

    [Fact]
    public void ConRutaCertificadoCAVaciaElArranqueNoCambia()
    {
        using var sp = Construir(new() { ["Motor:Embeddings:RutaCertificadoCA"] = "" });

        // No lanza OptionsValidationException: vacio sigue arrancando igual que el perfil TEI local de hoy.
        var opciones = sp.GetRequiredService<IOptions<MotorOptions>>().Value;

        Assert.True(string.IsNullOrEmpty(opciones.Embeddings.RutaCertificadoCA));
    }

    [Fact]
    public void ConRutaCertificadoCAExistenteElArranqueNoFalla()
    {
        var archivoTemporal = Path.GetTempFileName();
        try
        {
            using var sp = Construir(new() { ["Motor:Embeddings:RutaCertificadoCA"] = archivoTemporal });

            var opciones = sp.GetRequiredService<IOptions<MotorOptions>>().Value;

            Assert.Equal(archivoTemporal, opciones.Embeddings.RutaCertificadoCA);
        }
        finally
        {
            File.Delete(archivoTemporal);
        }
    }
}
