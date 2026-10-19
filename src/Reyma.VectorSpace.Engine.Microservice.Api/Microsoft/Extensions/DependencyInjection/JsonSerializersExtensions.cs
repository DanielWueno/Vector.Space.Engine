using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class JsonSerializersExtensions
    {
        internal static void AddReymaJsonSerializersDefault(this IServiceCollection services, IWebHostEnvironment env)
        {
            services.Configure<JsonOptions>(opts =>
            {
                opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                opts.JsonSerializerOptions.WriteIndented = !env.IsProduction();
            });

            services.Configure<MvcNewtonsoftJsonOptions>(opts =>
            {
                opts.SerializerSettings.Converters.Add(new StringEnumConverter());
                opts.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                opts.SerializerSettings.Formatting = !env.IsProduction() ? Formatting.Indented : Formatting.None;
            });
        }
    }
}
