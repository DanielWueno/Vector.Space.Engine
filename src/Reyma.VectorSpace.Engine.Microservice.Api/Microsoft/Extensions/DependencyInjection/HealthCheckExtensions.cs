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

        internal static void AddHealthChecks(this IServiceCollection services, string connectionString)
        {
            services.AddHealthChecks()
                .AddCheck("Self", () => HealthCheckResult.Healthy())
                .AddNpgSql(connectionString, name: "SQL Database", tags: new string[] { "db", TagServices });
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
