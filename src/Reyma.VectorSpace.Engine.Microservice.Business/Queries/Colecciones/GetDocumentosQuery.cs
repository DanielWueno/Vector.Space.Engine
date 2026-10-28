using MediatR;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Queries.Colecciones;

/// <summary>
/// Lista los documentos indexados en una coleccion (item 1.19): ruta, huella (<c>document_fingerprint</c>) y
/// cantidad de puntos de cada uno, sin acceso directo a Qdrant. Hallazgo al revisar el barrido de documentos
/// ausentes del origen 'proyecto' (1.7.5) y la busqueda de puntos existentes de la ingesta (ninguno de los dos
/// expone un listado legible por coleccion, ver <see cref="IMotorDeColecciones.ListarDocumentosAsync"/>). Nunca
/// 404: coleccion inexistente responde con una lista vacia (mismo criterio que <see cref="GetColeccionesQuery"/>).
/// </summary>
public sealed record GetDocumentosQuery(string Coleccion) : IRequest<IReadOnlyList<DocumentoIndexadoResponse>>
{
    public sealed class GetDocumentosQueryHandler(IMotorDeColecciones motor)
        : IRequestHandler<GetDocumentosQuery, IReadOnlyList<DocumentoIndexadoResponse>>
    {
        public Task<IReadOnlyList<DocumentoIndexadoResponse>> Handle(GetDocumentosQuery request, CancellationToken cancellationToken) =>
            motor.ListarDocumentosAsync(request.Coleccion, cancellationToken);
    }
}
