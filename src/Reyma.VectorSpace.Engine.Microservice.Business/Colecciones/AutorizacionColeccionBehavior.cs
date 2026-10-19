using MediatR;
using Reyma.Exceptions.Api;
using Reyma.IdentityService.Tools.Abstractions;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;

/// <summary>
/// Multi-tenencia por coleccion: niega con 403 toda <see cref="ISolicitudDeColeccion"/> cuya coleccion y
/// operacion no otorgue la tabla de acceso al usuario en sesion. Corre antes que la validacion del cuerpo, asi que
/// quien no tiene acceso recibe 403 sin importar lo que envie.
/// </summary>
public sealed class AutorizacionColeccionBehavior<TRequest, TResponse>(
    ICurrentUserAccessor usuarioActual, IAccesoColecciones acceso)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is ISolicitudDeColeccion solicitud
            && !acceso.Permite(usuarioActual.User, solicitud.Coleccion, solicitud.Operacion))
            throw new ForbiddenException();

        return next(cancellationToken);
    }
}
