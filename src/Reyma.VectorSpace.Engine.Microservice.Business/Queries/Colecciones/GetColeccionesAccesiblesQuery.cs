using MediatR;
using Reyma.IdentityService.Tools.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

/// <summary>
/// Solo las colecciones que el usuario en sesion alcanza; las de otros tenants no aparecen ni por nombre.
/// </summary>
public sealed record GetColeccionesAccesiblesQuery : IRequest<ColeccionesAccesiblesResponse>
{
    public sealed class GetColeccionesAccesiblesQueryHandler(ICurrentUserAccessor usuarioActual, IAccesoColecciones acceso)
        : IRequestHandler<GetColeccionesAccesiblesQuery, ColeccionesAccesiblesResponse>
    {
        public Task<ColeccionesAccesiblesResponse> Handle(GetColeccionesAccesiblesQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(new ColeccionesAccesiblesResponse(acceso.Accesibles(usuarioActual.User)));
    }
}
