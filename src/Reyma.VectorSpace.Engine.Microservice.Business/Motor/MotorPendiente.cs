using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Motor;

/// <summary>
/// Implementacion registrada mientras el motor no este portado: responde 501 despues de que la solicitud ya paso
/// autenticacion, control de acceso y validacion.
/// </summary>
public sealed class MotorPendiente : IMotorDeEmbeddings, IMotorDeBusqueda, IMotorDeIngesta
{
    private const string Mensaje = "El motor de embeddings/ingesta todavia no esta portado a este servicio (item 1.4).";

    public Task<EmbeddingsResponse> CalcularAsync(string coleccion, IReadOnlyList<string> textos, CancellationToken cancellationToken) =>
        throw new NoImplementadoException(Mensaje);

    public Task<BusquedaResponse> BuscarAsync(string coleccion, string consulta, int topK, CancellationToken cancellationToken) =>
        throw new NoImplementadoException(Mensaje);

    public Task<IngestaResponse> IngerirAsync(string coleccion, IReadOnlyList<DocumentoRequest> documentos, CancellationToken cancellationToken) =>
        throw new NoImplementadoException(Mensaje);
}
