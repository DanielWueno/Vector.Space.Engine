namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Reyma.VectorSpace.Engine.Microservice.Business;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

[ExcludeFromCodeCoverage]
internal static class StartupExtensions
{
    private const string DefaultCorsPolicy = "DefaultCorsPolicy";

    public static void AddReymaDefaults(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        services.AddOptions();

        services.AddReymaAutoMapper(opts => { opts.Assemblies = new Assembly[] { typeof(BusinessAssembly).Assembly }; });
        // Item (TransactionPipelineBehavior): se usa AddReymaMediator (Reyma.Mediator.Base.DependencyInjection)
        // en vez de AddReymaMediatorDefault (Reyma.Mediator.Base.Postgres.DependencyInjection) a proposito.
        // AddReymaMediatorDefault agrega ademas TransactionPipelineBehavior, que envuelve cada solicitud en un
        // TransactionScope de hasta TransactionManager.MaximumTimeout (10 min). Este servicio no tiene ningun
        // recurso transaccional de .NET (Qdrant y el sidecar de embeddings no participan en TransactionScope);
        // ese behavior solo aportaba un timeout artificial. La ingesta de innovapp-docs (629 chunks, una sola
        // solicitud, TEI en CPU) tardo mas de 10 minutos, guardo los puntos correctamente en Qdrant pero la
        // solicitud HTTP respondio 500 "The transaction has aborted." al expirar el scope. Ver
        // docs/plan/ejecucion-plan.estado.json (item 1.4) y Tests/Mediator/MediatorPipelineTests.
        services.AddReymaMediator(typeof(BusinessAssembly).Assembly);

        services.AddDomainManager();

        services.AddReymaFluentValidations(typeof(BusinessAssembly).Assembly);
        // Item 1.9: este servicio guarda vectores en Qdrant, no en Postgres ni en ninguna base relacional.
        // Se retiro AddReymaPersistence/AddNpgSql y la exigencia de CONNECTION_STRING; ver docs/servicio/README.md.
        services.AddReymaPermissionsValidator(config);
        services.AddHttpContextAccessor();
        services.AddReymaIdentityServiceTools();
        // El reloj de Reyma.Dates.Base.Postgres consulta SQL; el servicio vacio no lo necesita.
        services.AddSingleton<Reyma.Dates.Abstractions.IDateTime, UtcDateTime>();
        services.AddReymaIdentityServiceAuthentication(config);
        services.AddReymaMicroserviceCommunication();
        services.AddReymaIntegrityService(config);

        services.AddCors(options =>
        {
            // H11: una lista vacia no debe lanzar ni permitir ningun origen (sin anadir "localhost" bajo depurador).
            var origins = (config.GetSection("AllowedOrigins").Get<string>() ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            options.AddPolicy(DefaultCorsPolicy, builder =>
            {
                if (origins.Length == 0)
                {
                    builder.WithOrigins();
                    return;
                }

                builder.AllowAnyHeader().AllowAnyMethod().AllowCredentials()
                       .SetIsOriginAllowedToAllowWildcardSubdomains().WithOrigins(origins);
            });
        });

        services.AddReymaCompression();
        services.AddReymaMvcDefaults();
        services.AddReymaJsonSerializersDefault(env);
        services.AddReymaRateLimit();
        services.AddReymaSwagger(config);
        services.AddServiceHealthChecks();
        services.AddLocalServices(config, env);
    }

    internal static void UseReymaDefaults(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        // DD7: ASPNETCORE_BASEPATH se lee por IConfiguration, no por Environment.GetEnvironmentVariable.
        var config = app.ApplicationServices.GetRequiredService<IConfiguration>();
        app.UsePathBase(config["ASPNETCORE_BASEPATH"]);

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseReymaSwagger(env);
        }

        app.UseCors(DefaultCorsPolicy);
        app.UseGraceterm();
        app.UseResponseCompression();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHealthCheckEndPoints();
            endpoints.MapControllers();
        });
    }
}
