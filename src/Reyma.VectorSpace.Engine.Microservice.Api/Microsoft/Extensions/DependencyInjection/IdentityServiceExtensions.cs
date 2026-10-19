namespace Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;

internal static class IdentityServiceExtensions
{
    internal static void AddReymaIdentityServiceAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var authority = config["IdentityServerAuthenticationOptions:Authority"];
        var apiName = config["IdentityServerAuthenticationOptions:ApiName"];
        var requireHttps = config.GetValue<bool>("IdentityServerAuthenticationOptions:RequireHttpsMetadata", true);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts =>
            {
                opts.Authority = authority;
                opts.Audience = apiName;
                opts.RequireHttpsMetadata = requireHttps;
                // Reyma.IdentityService.Extensions lee los claims crudos ("sub", "role"); con el mapeo por defecto
                // JwtBearer los renombra a los URI de Microsoft y GetUserId/GetRoles devuelven vacio.
                opts.MapInboundClaims = false;
                opts.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                { ValidateAudience = true, ValidateIssuer = true, ValidateLifetime = true };
            });
    }
}
