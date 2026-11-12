using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Catalogo;

/// <summary>Valores de <c>perfilIngesta</c>: la misma lista con la que se valida (<see cref="PerfilDeIngesta.Catalogo"/>).</summary>
public sealed record GetPerfilesIngestaQuery : IRequest<IReadOnlyList<PerfilIngestaInfoResponse>>
{
    public sealed class GetPerfilesIngestaQueryHandler
        : IRequestHandler<GetPerfilesIngestaQuery, IReadOnlyList<PerfilIngestaInfoResponse>>
    {
        public Task<IReadOnlyList<PerfilIngestaInfoResponse>> Handle(GetPerfilesIngestaQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(PerfilDeIngesta.Catalogo);
    }
}
