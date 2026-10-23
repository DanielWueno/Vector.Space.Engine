using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Net;

namespace Reyma.VectorSpace.Engine.Microservice.Api.Filters
{
    /// <summary>
    ///
    /// </summary>
    [ExcludeFromCodeCoverage]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ReymaExceptionFilterAttribute : ExceptionFilterAttribute
    {
        private const string DefaultContentType = "application/json";

        private readonly IHostEnvironment _env;
        private readonly ApiBehaviorOptions _options;
        private readonly ILogger<ReymaExceptionFilterAttribute> _logger;

        /// <summary>
        ///
        /// </summary>
        /// <param name="env"></param>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        public ReymaExceptionFilterAttribute(IHostEnvironment env, IOptions<ApiBehaviorOptions> options, ILogger<ReymaExceptionFilterAttribute> logger)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="context"></param>
        public override void OnException(ExceptionContext context)
        {
            dynamic result = new ExpandoObject();

            var code = HttpStatusCode.InternalServerError;

            switch (context.Exception)
            {
                case Exception ex when ex is BadRequestException:
                    result.errors = new[] { ex.Message };
                    code = HttpStatusCode.BadRequest;
                    break;
                case Exception ex when ex is ConflictException:
                    result.errors = new[] { ex.Message };
                    code = HttpStatusCode.Conflict;
                    break;
                case Exception ex when ex is UnauthorizedException:
                    code = HttpStatusCode.Unauthorized;

                    if (ex.Message.StartsWith("User"))
                        result.errors = new[] { ex.Message };
                    break;
                case Exception ex when ex is ForbiddenException:
                    code = HttpStatusCode.Forbidden;

                    if (ex.Message.StartsWith("User"))
                        result.errors = new[] { ex.Message };
                    break;
                case Exception ex when ex is NotFoundException:
                    result.errors = new[] { ex.Message };
                    code = HttpStatusCode.NotFound;
                    break;
                case Exception ex when ex is NoImplementadoException:
                    result.errors = new[] { ex.Message };
                    code = HttpStatusCode.NotImplemented;
                    break;
                case Exception ex when ex is MotorNoDisponibleException:
                    // El detalle de la dependencia ya quedo en el log de quien la lanzo; el mensaje es para el cliente.
                    result.errors = new[] { ex.Message };
                    code = HttpStatusCode.ServiceUnavailable;
                    break;
                case IngestasSaturadasException ex:
                    // 1.7: limite de admision de ingestas; el cliente reintenta despues de Retry-After.
                    result.errors = new[] { ex.Message };
                    code = HttpStatusCode.TooManyRequests;
                    context.HttpContext.Response.Headers.RetryAfter = ex.SegundosParaReintentar.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case BadHttpRequestException ex when ex.StatusCode == StatusCodes.Status413PayloadTooLarge:
                    // 1.7.1: Kestrel corta la lectura del cuerpo al superar el limite de la accion (RequestSizeLimit).
                    result.errors = new[] { "El cuerpo de la solicitud supera el limite permitido." };
                    code = HttpStatusCode.RequestEntityTooLarge;
                    break;
                default:
                    _logger.LogError(context.Exception, context.Exception.Message);
                    result.errors = new[] { context.Exception.Message };
                    break;
            }

            // Use 'Problem Details' feature added in 2.1 and 2.2 so it follows the standardized error messages in API controllers. It's based on the RFC 7807 specification.
            // ASP.NET Core 2.1 Problem Details support: https://devblogs.microsoft.com/aspnet/asp-net-core-2-1-web-apis/#problem-details
            // ASP.NET Core 2.2 release notes on Problem Details: https://blogs.msdn.microsoft.com/webdev/2018/09/12/asp-net-core-2-2-0-preview2-now-available/
            if (_options.ClientErrorMapping.TryGetValue((int)code, out var errorData))
            {
                result.type = errorData.Link;
                result.title = errorData.Title;
            }
            else
            {
                var reason = ReasonPhrases.GetReasonPhrase((int)code);
                if (!string.IsNullOrWhiteSpace(reason))
                    result.title = reason;
            }

            result.status = (int)code;
            result.traceId = context.HttpContext.TraceIdentifier;

            context.HttpContext.Response.ContentType = DefaultContentType;
            context.HttpContext.Response.StatusCode = (int)code;


            context.Result = new JsonResult(result);
        }
    }
}
