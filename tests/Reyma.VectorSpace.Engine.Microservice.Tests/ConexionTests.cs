using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

[Collection("Api")]
public sealed class ConexionTests(ApiFactory factory)
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ArranqueConectaElContextoALaConfiguracionDelEntorno(string environment)
    {
        var settings = DevelopmentConfiguration().AsEnumerable().ToDictionary(pair => pair.Key, pair => pair.Value);
        settings["CONNECTION_STRING"] = ApiFactory.ConnectionString;
        var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });
        using var scope = configured.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var connection = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString();

        if (environment == Environments.Development)
        {
            Assert.Equal(config.GetConnectionString("Default"), connection);
            var parsed = new NpgsqlConnectionStringBuilder(connection);
            Assert.Equal(settings["DatabaseConnection:Password"], parsed.Password);
            Assert.Equal(ServicioInfo.Nombre, parsed.ApplicationName);
        }
        else
        {
            Assert.Equal(ApiFactory.ConnectionString, connection);
        }
    }

    [Fact]
    public void ProductionNoArrancaSinConnectionString()
    {
        var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CONNECTION_STRING"] = "",
                    ["ConnectionStrings:Default"] = ApiFactory.ConnectionString
                }));
        });

        var exception = Assert.Throws<InvalidOperationException>(() => configured.CreateClient());
        Assert.Equal("Falta la configuracion requerida: 'CONNECTION_STRING'.", exception.Message);
    }

    [Fact]
    public void DevelopmentComponeCadenaConValoresPorDefecto()
    {
        var config = DevelopmentConfiguration();
        var connection = config.GetReymaConnectionString(Environment(Environments.Development));
        var parsed = new NpgsqlConnectionStringBuilder(connection);

        Assert.Equal(config["DatabaseConnection:Host"], parsed.Host);
        Assert.Equal(config["DatabaseConnection:Database"], parsed.Database);
        Assert.Equal(config["DatabaseConnection:UserId"], parsed.Username);
        Assert.Equal(config["DatabaseConnection:Password"], parsed.Password);
        Assert.Equal(5432, parsed.Port);
        Assert.True(parsed.Pooling);
        Assert.Equal(ServicioInfo.Nombre, parsed.ApplicationName);
        Assert.Equal(connection, config.GetConnectionString("Default"));
    }

    [Fact]
    public void DevelopmentUsaConnectionStringsDefaultSinPartesDatabaseConnection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ApiFactory.ConnectionString
        }).Build();

        Assert.Equal(ApiFactory.ConnectionString, config.GetReymaConnectionString(Environment(Environments.Development)));
    }

    [Fact]
    public void DevelopmentSinNingunaCadenaNombraLasClavesAusentes()
    {
        var config = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            config.GetReymaConnectionString(Environment(Environments.Development)));

        Assert.StartsWith("Falta la configuracion requerida: DatabaseConnection:Host", exception.Message);
    }

    [Fact]
    public void DevelopmentRespetaOpcionesYEscapaValores()
    {
        var config = DevelopmentConfiguration();
        config["DatabaseConnection:Port"] = "5433";
        config["DatabaseConnection:Pooling"] = "false";
        config["DatabaseConnection:ApplicationName"] = "prueba-conexion";
        config["DatabaseConnection:Password"] = Guid.NewGuid() + ";con=separadores";
        config["CONNECTION_STRING"] = "ignorada-en-development";
        var parsed = new NpgsqlConnectionStringBuilder(
            config.GetReymaConnectionString(Environment(Environments.Development)));

        Assert.Equal(5433, parsed.Port);
        Assert.False(parsed.Pooling);
        Assert.Equal("prueba-conexion", parsed.ApplicationName);
        Assert.Equal(config["DatabaseConnection:Password"], parsed.Password);
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Database")]
    [InlineData("UserId")]
    [InlineData("Password")]
    public void DevelopmentNombraLaClaveAusenteSinExponerValores(string field)
    {
        var config = DevelopmentConfiguration();
        config[$"DatabaseConnection:{field}"] = " ";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            config.GetReymaConnectionString(Environment(Environments.Development)));

        Assert.Equal($"Falta la configuracion requerida: DatabaseConnection:{field}.", exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void OtrosEntornosUsanSoloConnectionString(string environment)
    {
        var config = DevelopmentConfiguration();
        config["CONNECTION_STRING"] = ApiFactory.ConnectionString;
        config["ConnectionStrings:Default"] = "no-usar";

        Assert.Equal(ApiFactory.ConnectionString, config.GetReymaConnectionString(Environment(environment)));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void OtrosEntornosNoUsanLaCadenaDefaultNiLaDeDevelopment(string environment)
    {
        var config = DevelopmentConfiguration();
        config["ConnectionStrings:Default"] = ApiFactory.ConnectionString;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            config.GetReymaConnectionString(Environment(environment)));

        Assert.Equal("Falta la configuracion requerida: 'CONNECTION_STRING'.", exception.Message);
    }

    private static IConfigurationRoot DevelopmentConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseConnection:Host"] = "database.invalid",
            ["DatabaseConnection:Database"] = "test-db",
            ["DatabaseConnection:UserId"] = "test-user",
            ["DatabaseConnection:Password"] = Guid.NewGuid().ToString()
        }).Build();

    private static IWebHostEnvironment Environment(string name) => new TestEnvironment
    {
        EnvironmentName = name
    };

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = ServicioInfo.Nombre;
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
