namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Persistence;
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

        var connectionString = config.GetReymaConnectionString(env);

        services.AddReymaAutoMapper(opts => { opts.Assemblies = new Assembly[] { typeof(BusinessAssembly).Assembly }; });
        services.AddReymaMediatorDefault(typeof(BusinessAssembly).Assembly);

        services.AddDomainManager();

        services.AddReymaFluentValidations(typeof(BusinessAssembly).Assembly);
        services.AddReymaPersistence<ApplicationDbContext>(connectionString, false, "Oid");
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
        services.AddHealthChecks(connectionString);
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
