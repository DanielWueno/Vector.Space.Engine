using MediatR;
using Reyma.IdentityService.Extensions;
using Reyma.IdentityService.Tools.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Sesion;

/// <summary>
/// Consulta de solo lectura con los datos del login del usuario en sesion (claims del token).
/// No toca la base de datos; sirve como ejemplo de uso de ICurrentUserAccessor.
/// </summary>
public sealed record GetUsuarioActualQuery : IRequest<UsuarioActualResponse>
{
    public sealed class GetUsuarioActualQueryHandler(ICurrentUserAccessor currentUserAccessor)
        : IRequestHandler<GetUsuarioActualQuery, UsuarioActualResponse>
    {
        public Task<UsuarioActualResponse> Handle(GetUsuarioActualQuery request, CancellationToken cancellationToken)
        {
            var user = currentUserAccessor.User;
            var roles = user?.GetRoles()?.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!.ToString()!).ToArray()
                        ?? [];

            return Task.FromResult(new UsuarioActualResponse(
                user?.GetUserId().ToString(),
                user?.GetUserName(),
                user?.GetIdentityEmployeeId().ToString(),
                user?.GetClientId().ToString(),
                roles));
        }
    }
}
