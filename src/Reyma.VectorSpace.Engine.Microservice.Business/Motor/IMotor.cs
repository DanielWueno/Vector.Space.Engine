using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Motor;

// Puertos del contrato HTTP hacia el motor. Los implementa 1.4 (pipeline de ingesta portado, SparseTokenizer,
// cliente HTTP del sidecar de embeddings y adaptador Qdrant); hasta entonces responde MotorPendiente.
// Reciben la coleccion ya autorizada: no repiten el control de acceso.

public interface IMotorDeEmbeddings
{
    Task<EmbeddingsResponse> CalcularAsync(string coleccion, IReadOnlyList<string> textos, CancellationToken cancellationToken);
}

public interface IMotorDeBusqueda
{
    Task<BusquedaResponse> BuscarAsync(string coleccion, string consulta, int topK, CancellationToken cancellationToken);
}

public interface IMotorDeIngesta
{
    Task<IngestaResponse> IngerirAsync(string coleccion, IReadOnlyList<DocumentoRequest> documentos, CancellationToken cancellationToken);
}
