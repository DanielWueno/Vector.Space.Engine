namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Reyma.VectorSpace.Engine.Microservice.Api.Constants;
using Reyma.Swagger.Extensions;
using System;
using System.IO;
using System.Linq;

internal static class SwaggerExtensions
{
    internal static void AddReymaSwagger(this IServiceCollection services, IConfiguration config)
    {
        var authority = config["IdentityServerAuthenticationOptions:Authority"];

        services.AddSwaggerGen(opts =>
        {
            opts.SwaggerDoc(SwaggerConstants.Docs.ApiVersion, new OpenApiInfo
            {
                Title = SwaggerConstants.Docs.ApiName,
                Version = SwaggerConstants.Docs.ApiVersion
            });

            // H13 / sin Authority: se omite la definicion OAuth en vez de construir una Uri relativa invalida.
            if (!string.IsNullOrWhiteSpace(authority))
            {
                opts.AddSecurityDefinition(SwaggerConstants.Docs.SecurityDefinition, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri($"{authority}{SwaggerConstants.OAuth.AuthorizeUrl}"),
                            TokenUrl = new Uri($"{authority}{SwaggerConstants.OAuth.TokenUrl}"),
                            Scopes = SwaggerConstants.OAuth.Scopes
                        }
                    }
                });
            }

            opts.ResolveConflictingActions(x => x.First());
            opts.OperationFilter<AuthorizeCheckOperationFilter>();
            opts.OperationFilter<RemoveVersionParametersFilter>();
            opts.DocumentFilter<SetVersionInPathsFilter>();

            foreach (var fileName in Directory.GetFiles(AppContext.BaseDirectory, "*.xml"))
                opts.IncludeXmlComments(fileName);
        });
    }

    internal static void UseReymaSwagger(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseSwagger(opts =>
        {
            opts.RouteTemplate = "/swagger/{documentName}/swagger.json";
        });

        app.UseSwaggerUI(opts =>
        {
            opts.DocumentTitle = SwaggerConstants.Docs.ApiName;
            opts.SwaggerEndpoint("./swagger/v1/swagger.json", SwaggerConstants.Docs.ApiName);
            opts.RoutePrefix = string.Empty;

            // Display
            opts.DefaultModelExpandDepth(3);
            opts.DefaultModelsExpandDepth(0);
            opts.DisplayRequestDuration();
            opts.EnableDeepLinking();
            opts.ShowExtensions();

            // H13: sin EnableValidator() -- no se envia la especificacion a un validador externo.

            var settings = app.ApplicationServices.GetRequiredService<IConfiguration>();
            var clientId = settings["SwaggerOptions:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                clientId = SwaggerConstants.OAuth.ClientId;
            var clientSecret = settings["SwaggerOptions:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientSecret))
                clientSecret = SwaggerConstants.OAuth.ClientSecret;

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                opts.OAuthClientId(clientId);
                if (!string.IsNullOrWhiteSpace(clientSecret))
                    opts.OAuthClientSecret(clientSecret);
                opts.OAuthAppName(SwaggerConstants.Docs.ApiName);
                opts.OAuthScopes(SwaggerConstants.OAuth.Scopes.Keys.ToArray());
                opts.OAuthUsePkce();
            }
        });
    }
}
