using Reyma.VectorSpace.Engine.Microservice.Business;
using System.Collections.Generic;

namespace Reyma.VectorSpace.Engine.Microservice.Api.Constants
{
    internal static class SwaggerConstants
    {
        internal static class Docs
        {
            internal static string ApiName => $"{ServicioInfo.Nombre} {ApiVersion}";

            internal static string ApiVersion => "v1";

            internal static string SecurityDefinition => "oauth2";
        }

        internal static class OAuth
        {
            internal static string TokenUrl => "/connect/token";

            internal static string AuthorizeUrl => "/connect/authorize";

            // Valores por defecto de la plantilla; SwaggerOptions:ClientId / ClientSecret los sobrescriben.
            internal static string ClientId => "swagger";

            internal static string ClientSecret => "sin-definir";

            internal static Dictionary<string, string> Scopes => new Dictionary<string, string>
            {
                { "permissions.service.read", "Read Access to Permissions Api" },
                { "integrity.service.read", "Read Access to Integrity Api" },
                { "integrity.service.write", "Write Access to Integrity Api" }
            };
        }
    }
}
