using Graceterm;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class HealthCheckExtensions
    {
        private const string TagServices = "services";

        internal static void AddServiceHealthChecks(this IServiceCollection services)
        {
            // Item 1.9: el servicio no usa Postgres (guarda vectores en Qdrant, aun no integrado -
            // eso llega en 1.4). Hoy no tiene dependencias externas reales que comprobar en el
            // readiness; cuando 1.4 agregue Qdrant/sidecar, su check se agrega aqui con tag "services".
            services.AddHealthChecks()
                .AddCheck("Self", () => HealthCheckResult.Healthy())
                .AddCheck("Dependencies", () => HealthCheckResult.Healthy(), tags: new string[] { TagServices });
        }

        internal static void MapHealthCheckEndPoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapHealthChecks("/health");
            endpoints.MapHealthChecks("/health/self", new HealthCheckOptions { Predicate = r => r.Name.Contains("Self") });
            endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains(TagServices) });
        }

        internal static void UseGraceterm(this IApplicationBuilder app)
        {
            app.UseGraceterm(opts => { opts.TimeoutSeconds = 16; opts.IgnorePaths("/health", "/health/self", "/health/ready"); });
        }
    }
}
