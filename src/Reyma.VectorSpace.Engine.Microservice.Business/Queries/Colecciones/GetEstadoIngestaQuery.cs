using MediatR;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

/// <summary>
/// Consulta del estado de una ingesta de proyecto (1.7.5, origen 'proyecto' desde .zip, asincrono). 404 si el
/// <c>ingestaId</c> no existe o pertenece a otra coleccion (no se distingue uno de otro: ninguno es asunto de quien
/// consulta).
/// </summary>
public sealed record GetEstadoIngestaQuery(string Coleccion, Guid IngestaId) : IRequest<IngestaResponse>
{
    public sealed class GetEstadoIngestaQueryHandler(IRegistroDeIngestasProyecto registro)
        : IRequestHandler<GetEstadoIngestaQuery, IngestaResponse>
    {
        public Task<IngestaResponse> Handle(GetEstadoIngestaQuery request, CancellationToken cancellationToken)
        {
            var estado = registro.Obtener(request.Coleccion, request.IngestaId)
                ?? throw new NotFoundException(nameof(IngestaResponse), request.IngestaId);
            return Task.FromResult(estado);
        }
    }
}
