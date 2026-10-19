using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Net.Mime;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class CompressionExtensions
    {
        internal static void AddReymaCompression(this IServiceCollection services)
        {
            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Optimal;
            });

            services.Configure<GzipCompressionProviderOptions>(opts =>
            {
                opts.Level = CompressionLevel.Optimal;
            });

            services.AddResponseCompression(opts =>
            {
                var general = new[]
                {
                    MediaTypeNames.Text.Plain,
                    MediaTypeNames.Text.Html,
                    "text/css",
                    "font/woff2",
                    "application/javascript",
                    MediaTypeNames.Application.Pdf,
                    "image/x-icon",
                    MediaTypeNames.Image.Jpeg,
                    "image/png"
                };

                opts.MimeTypes = general;
                opts.EnableForHttps = true;
                opts.Providers.Add<BrotliCompressionProvider>();
                opts.Providers.Add<GzipCompressionProvider>();
            });
        }
    }
}
