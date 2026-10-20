using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Api.Filters;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // La factory padre libera sus derivadas al final: Graceterm comparte el estado de apagado entre hosts.
    public ErrorLogger Errors { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // No heredar variables, user-secrets ni configuraciones locales de quien ejecuta las pruebas.
            config.Sources.Clear();
            config.AddJsonFile("appsettings.json", optional: false);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_BASEPATH"] = "",
                ["AllowedOrigins"] = "",
                ["IdentityServerAuthenticationOptions:Authority"] = "",
                ["PermissionsValidatorOptions:ApiUri"] = "",
                ["IntegrityServiceOptions:ApiUri"] = "",
                ["Serilog:MinimumLevel:Default"] = "Fatal",
                ["Serilog:MinimumLevel:Override:Microsoft"] = "Fatal",
                ["Serilog:MinimumLevel:Override:System"] = "Fatal"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ErrorPruebaController).Assembly);
            services.AddSingleton<ILogger<ReymaExceptionFilterAttribute>>(Errors);
        });
    }

    public sealed class ErrorLogger : ILogger<ReymaExceptionFilterAttribute>
    {
        public ConcurrentQueue<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, exception, formatter(state, exception)));
    }
}

[CollectionDefinition("Api", DisableParallelization = true)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
