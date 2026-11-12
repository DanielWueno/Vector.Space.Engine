using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
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

    // 1.7.4: perfiles de embeddings adicionales (p. ej. el perfil AltheIA/bge-m3 de 1.7.3), con un HttpClient
    // nombrado propio, registrados junto al perfil por defecto (tei-arctic-256, descrito por Motor:Embeddings).

    private static ServiceProvider ConstruirConPerfilAdicional() => Construir(new()
    {
        ["Motor:Embeddings:Perfiles:altheia-bge-m3-256:UrlBase"] = "https://altheia.invalid",
        ["Motor:Embeddings:Perfiles:altheia-bge-m3-256:Modelo"] = "bge-m3",
        ["Motor:Embeddings:Perfiles:altheia-bge-m3-256:Dimension"] = "256",
        ["Motor:Embeddings:Perfiles:altheia-bge-m3-256:PrefijoConsulta"] = "",
        ["Motor:Embeddings:Perfiles:altheia-bge-m3-256:ApiKey"] = "clave-de-prueba",
    });

    [Fact]
    public void ElFactoryResuelveElPerfilPorDefectoSinPedirNinguno()
    {
        using var sp = ConstruirConPerfilAdicional();
        var factory = sp.GetRequiredService<IVectorizationBrainFactory>();

        var resuelto = factory.Resolver(null);

        Assert.Equal(MotorOptions.PerfilEmbeddingsPorDefecto, resuelto.Nombre);
        Assert.Same(sp.GetRequiredService<IVectorizationBrain>(), resuelto.Brain);
    }

    [Fact]
    public void ElFactoryResuelveUnPerfilAdicionalConSuPropioModeloYDimension()
    {
        using var sp = ConstruirConPerfilAdicional();
        var factory = sp.GetRequiredService<IVectorizationBrainFactory>();

        var resuelto = factory.Resolver("altheia-bge-m3-256");

        Assert.Equal("altheia-bge-m3-256", resuelto.Nombre);
        Assert.Equal("bge-m3", resuelto.Brain.ModelName);
        Assert.Equal(256, resuelto.Brain.EmbeddingDimensions);
        Assert.NotSame(sp.GetRequiredService<IVectorizationBrain>(), resuelto.Brain);
    }

    [Fact]
    public void ElPerfilAdicionalTieneSuPropioHttpClienteConLaClaveBearer()
    {
        using var sp = ConstruirConPerfilAdicional();

        var cliente = sp.GetRequiredService<IHttpClientFactory>().CreateClient(VectorizationBrainFactory.NombreCliente("altheia-bge-m3-256"));

        Assert.Equal(new Uri("https://altheia.invalid/"), cliente.BaseAddress);
        Assert.Equal("Bearer", cliente.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("clave-de-prueba", cliente.DefaultRequestHeaders.Authorization!.Parameter);
    }

    [Fact]
    public void UnPerfilNoRegistradoLanzaBadRequestConLosPerfilesDisponibles()
    {
        using var sp = ConstruirConPerfilAdicional();
        var factory = sp.GetRequiredService<IVectorizationBrainFactory>();

        var ex = Assert.Throws<BadRequestException>(() => factory.Resolver("perfil-que-no-existe"));

        Assert.Contains("perfil-que-no-existe", ex.Message);
        Assert.Contains(MotorOptions.PerfilEmbeddingsPorDefecto, ex.Message);
        Assert.Contains("altheia-bge-m3-256", ex.Message);
    }

    [Fact]
    public void RedefinirElPerfilPorDefectoComoAdicionalImpideArrancar()
    {
        using var sp = Construir(new()
        {
            [$"Motor:Embeddings:Perfiles:{MotorOptions.PerfilEmbeddingsPorDefecto}:UrlBase"] = "http://otro.invalid",
            [$"Motor:Embeddings:Perfiles:{MotorOptions.PerfilEmbeddingsPorDefecto}:Modelo"] = "otro-modelo",
            [$"Motor:Embeddings:Perfiles:{MotorOptions.PerfilEmbeddingsPorDefecto}:Dimension"] = "256",
        });

        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<MotorOptions>>().Value);
    }

    // Perfiles de LLM (perfilLlm de las ingestas): el por defecto lo describe Motor:Llm; los adicionales heredan de el
    // servidor, clave y CA, para no repetir la clave de AltheIA por cada modelo.

    private static ServiceProvider ConstruirConPerfilLlmAdicional() => Construir(new()
    {
        ["Motor:Llm:UrlBase"] = "https://altheia.invalid",
        ["Motor:Llm:ApiKey"] = "clave-llm-de-prueba",
        ["Motor:Llm:Perfiles:altheia-qwen-fast:Modelo"] = "qwen3.8-27b-fast",
    });

    [Fact]
    public void ElPerfilDeLlmPorDefectoEsDeepSinRazonar()
    {
        using var sp = ConstruirConPerfilLlmAdicional();
        var factory = sp.GetRequiredService<ILlmResumenBrainFactory>();
        var opciones = sp.GetRequiredService<IOptions<MotorOptions>>().Value.Llm;

        var resuelto = factory.Resolver(null);

        Assert.Equal("altheia-qwen-deep", resuelto.Nombre);
        Assert.Equal("qwen3.8-27b-deep", resuelto.Brain.ModelName);
        Assert.False(opciones.EnableThinking); // 1.7.3: deep sin enable_thinking=false responde vacio (0/30)
        Assert.Same(sp.GetRequiredService<ILlmResumenBrain>(), resuelto.Brain);
    }

    [Fact]
    public void UnPerfilDeLlmAdicionalHeredaServidorYClaveDelPorDefecto()
    {
        using var sp = ConstruirConPerfilLlmAdicional();

        var resuelto = sp.GetRequiredService<ILlmResumenBrainFactory>().Resolver("altheia-qwen-fast");
        var cliente = sp.GetRequiredService<IHttpClientFactory>().CreateClient(LlmResumenBrainFactory.NombreCliente("altheia-qwen-fast"));

        Assert.Equal("qwen3.8-27b-fast", resuelto.Brain.ModelName);
        Assert.Equal(new Uri("https://altheia.invalid/"), cliente.BaseAddress);
        Assert.Equal("clave-llm-de-prueba", cliente.DefaultRequestHeaders.Authorization!.Parameter);
        // EnableThinking no se hereda: fast no lo necesita y el por defecto (deep) lo tiene en false.
        Assert.Null(sp.GetRequiredService<IOptions<MotorOptions>>().Value.Llm.Efectivo("altheia-qwen-fast").EnableThinking);
    }

    [Fact]
    public void UnPerfilDeLlmNoRegistradoLanzaBadRequestConLosPerfilesDisponibles()
    {
        using var sp = ConstruirConPerfilLlmAdicional();

        var ex = Assert.Throws<BadRequestException>(() => sp.GetRequiredService<ILlmResumenBrainFactory>().Resolver("no-existe"));

        Assert.Contains("no-existe", ex.Message);
        Assert.Contains("altheia-qwen-deep", ex.Message);
        Assert.Contains("altheia-qwen-fast", ex.Message);
    }

    [Theory]
    [InlineData("Motor:Llm:Perfiles:altheia-qwen-deep:Modelo", "otro")]   // redefine el nombre del por defecto
    [InlineData("Motor:Llm:Perfiles:sin-modelo:MaxTokens", "160")]        // perfil adicional sin Modelo
    public void UnPerfilDeLlmMalDeclaradoImpideArrancar(string clave, string valor)
    {
        using var sp = Construir(new() { [clave] = valor });

        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<MotorOptions>>().Value);
    }
}
