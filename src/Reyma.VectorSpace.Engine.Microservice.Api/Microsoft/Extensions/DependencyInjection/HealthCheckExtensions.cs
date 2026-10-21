using Graceterm;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class HealthCheckExtensions
    {
        private const string TagServices = "services";

        internal static void AddServiceHealthChecks(this IServiceCollection services)
        {
            // Item 1.9 retiro Postgres; item 1.4: el readiness comprueba las dependencias reales del motor
            // (Qdrant y el sidecar de embeddings).
            services.AddHealthChecks()
                .AddCheck("Self", () => HealthCheckResult.Healthy())
                .AddMotorVectorialChecks(TagServices);
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
