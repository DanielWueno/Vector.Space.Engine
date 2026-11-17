using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Catalogo;

/// <summary>Perfiles de LLM (resumen de negocio) que acepta esta instancia, con su disponibilidad en vivo.</summary>
public sealed record GetPerfilesLlmQuery : IRequest<IReadOnlyList<PerfilLlmInfoResponse>>
{
    public sealed class GetPerfilesLlmQueryHandler(IMotorDeCatalogo catalogo)
        : IRequestHandler<GetPerfilesLlmQuery, IReadOnlyList<PerfilLlmInfoResponse>>
    {
        public Task<IReadOnlyList<PerfilLlmInfoResponse>> Handle(GetPerfilesLlmQuery request, CancellationToken cancellationToken) =>
            catalogo.ListarPerfilesLlmAsync(cancellationToken);
    }
}
