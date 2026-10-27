using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

/// <summary>
/// Lista las ingestas de proyecto registradas, en cualquier estado (item 1.15): cierra el hueco de que el
/// <c>ingestaId</c> de <see cref="IngerirProyectoZipCommand"/>/<see cref="IngerirProyectoGitCommand"/> solo era
/// visible en la respuesta 202, sin forma de recuperarlo si se perdia. <see cref="Coleccion"/> es opcional (item
/// 1.16, para cuando tampoco se sabe que coleccion buscar, ver <see cref="GetColeccionesQuery"/>): null o vacio trae
/// las ingestas de cualquier coleccion; con un valor, solo las de esa coleccion. Nunca 404 (a diferencia de
/// <see cref="GetEstadoIngestaQuery"/>): sin resultados responde con una lista vacia. Mismo alcance en memoria que el
/// resto del registro (ver <see cref="IRegistroDeIngestasProyecto"/>).
/// </summary>
public sealed record GetIngestasQuery(string? Coleccion = null) : IRequest<IReadOnlyList<IngestaResponse>>
{
    public sealed class GetIngestasQueryHandler(IRegistroDeIngestasProyecto registro)
        : IRequestHandler<GetIngestasQuery, IReadOnlyList<IngestaResponse>>
    {
        public Task<IReadOnlyList<IngestaResponse>> Handle(GetIngestasQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(registro.Listar(request.Coleccion));
    }
}
