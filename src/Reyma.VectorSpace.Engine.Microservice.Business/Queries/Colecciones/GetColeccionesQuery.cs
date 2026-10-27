using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

/// <summary>
/// Lista las colecciones existentes en el vector store, con su perfil de embeddings si lo tiene guardado (item
/// 1.16): hallazgo al revisar <see cref="GetIngestasQuery"/> -- ninguna de las dos superficies anteriores
/// (el registro de ingestas en memoria, por instancia, o conocer el nombre de antemano) permitia descubrir que
/// colecciones hay sin acceso directo a Qdrant. Nunca 404: sin colecciones responde con una lista vacia.
/// </summary>
public sealed record GetColeccionesQuery : IRequest<IReadOnlyList<ColeccionInfoResponse>>
{
    public sealed class GetColeccionesQueryHandler(IMotorDeColecciones motor)
        : IRequestHandler<GetColeccionesQuery, IReadOnlyList<ColeccionInfoResponse>>
    {
        public Task<IReadOnlyList<ColeccionInfoResponse>> Handle(GetColeccionesQuery request, CancellationToken cancellationToken) =>
            motor.ListarAsync(cancellationToken);
    }
}
