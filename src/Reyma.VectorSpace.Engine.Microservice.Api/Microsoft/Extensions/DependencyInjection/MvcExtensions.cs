using Microsoft.AspNetCore.Mvc;
using Reyma.VectorSpace.Engine.Microservice.Api.Filters;
using Reyma.Mvc.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class MvcExtensions
    {
        internal static void AddReymaMvcDefaults(this IServiceCollection services)
        {
            services.AddRouting(opts => opts.LowercaseUrls = true);
            services.AddMvc()
                    .AddNewtonsoftJson();

            services.Configure<MvcOptions>(opts =>
            {
                opts.UseGlobalRoutePrefix("api/");
                opts.Filters.Add(typeof(ReymaExceptionFilterAttribute));
            });
        }
    }
}
