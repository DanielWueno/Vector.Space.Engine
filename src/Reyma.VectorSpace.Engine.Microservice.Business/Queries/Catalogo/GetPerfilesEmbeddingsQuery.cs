using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Catalogo;

/// <summary>Perfiles de embeddings que acepta esta instancia, con su disponibilidad en vivo.</summary>
public sealed record GetPerfilesEmbeddingsQuery : IRequest<IReadOnlyList<PerfilEmbeddingsInfoResponse>>
{
    public sealed class GetPerfilesEmbeddingsQueryHandler(IMotorDeCatalogo catalogo)
        : IRequestHandler<GetPerfilesEmbeddingsQuery, IReadOnlyList<PerfilEmbeddingsInfoResponse>>
    {
        public Task<IReadOnlyList<PerfilEmbeddingsInfoResponse>> Handle(GetPerfilesEmbeddingsQuery request, CancellationToken cancellationToken) =>
            catalogo.ListarPerfilesEmbeddingsAsync(cancellationToken);
    }
}
