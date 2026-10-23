using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Motor;

// Puertos del contrato HTTP hacia el motor. Los implementa MotorVectorial en el proyecto Infrastructure (item 1.4:
// pipeline de ingesta portado, SparseTokenizer, cliente HTTP del sidecar de embeddings y adaptador Qdrant).
// Reciben la coleccion ya autorizada: no repiten el control de acceso.

// perfil: nombre del perfil de embeddings a usar (1.7.4); null = perfil por defecto de la coleccion/instancia.

public interface IMotorDeEmbeddings
{
    Task<EmbeddingsResponse> CalcularAsync(string coleccion, IReadOnlyList<string> textos, string? perfil, CancellationToken cancellationToken);
}

public interface IMotorDeBusqueda
{
    Task<BusquedaResponse> BuscarAsync(string coleccion, string consulta, int topK, string? perfil, CancellationToken cancellationToken);
}

public interface IMotorDeIngesta
{
    Task<IngestaResponse> IngerirAsync(string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, CancellationToken cancellationToken);
}
