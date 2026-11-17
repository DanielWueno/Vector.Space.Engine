using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Catalogo;

/// <summary>Origenes Git desde los que esta instancia acepta clonar (la misma lista blanca que valida cada URL).</summary>
public sealed record GetOrigenesGitQuery : IRequest<IReadOnlyList<OrigenGitInfoResponse>>
{
    public sealed class GetOrigenesGitQueryHandler(IMotorDeCatalogo catalogo)
        : IRequestHandler<GetOrigenesGitQuery, IReadOnlyList<OrigenGitInfoResponse>>
    {
        public Task<IReadOnlyList<OrigenGitInfoResponse>> Handle(GetOrigenesGitQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(catalogo.ListarOrigenesGit());
    }
}
