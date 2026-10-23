using Grpc.Core;
using Microsoft.Extensions.Logging;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Ingestion;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure;

/// <summary>
/// Implementa los puertos del contrato (<see cref="IMotorDeEmbeddings"/>, <see cref="IMotorDeBusqueda"/>,
/// <see cref="IMotorDeIngesta"/>) con el motor portado. Recibe la coleccion ya autorizada. Los fallos de Qdrant se
/// traducen a 503 sin exponer al cliente el detalle de la conexion; un perfil de embeddings no registrado o que choca
/// con el de la coleccion (1.7.4) se traduce a 400/409 respectivamente, nunca a 503.
/// </summary>
public sealed class MotorVectorial : IMotorDeEmbeddings, IMotorDeBusqueda, IMotorDeIngesta
{
    private readonly IVectorizationBrainFactory _brainFactory;
    private readonly QdrantVectorStore _store;
    private readonly QdrantSemanticRetriever _retriever;
    private readonly DocumentIngestionPipeline _pipeline;
    private readonly ILogger<MotorVectorial> _logger;

    public MotorVectorial(
        IVectorizationBrainFactory brainFactory,
        QdrantVectorStore store,
        QdrantSemanticRetriever retriever,
        DocumentIngestionPipeline pipeline,
        ILogger<MotorVectorial> logger)
    {
        _brainFactory = brainFactory;
        _store = store;
        _retriever = retriever;
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <summary>Vectores de documento (sin prefijo de consulta): los mismos que se guardarian al ingestar esos textos.</summary>
    public Task<EmbeddingsResponse> CalcularAsync(string coleccion, IReadOnlyList<string> textos, string? perfil, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var resuelto = _brainFactory.Resolver(perfil);
            await _store.VerificarPerfilParaConsultaAsync(coleccion, resuelto.Nombre, resuelto.Brain.ModelName, resuelto.Brain.EmbeddingDimensions, cancellationToken);
            var vectores = await resuelto.Brain.EmbedDocumentsAsync(textos, cancellationToken);
            return new EmbeddingsResponse(coleccion, resuelto.Brain.ModelName, resuelto.Brain.EmbeddingDimensions, vectores);
        });

    public Task<BusquedaResponse> BuscarAsync(string coleccion, string consulta, int topK, string? perfil, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var hits = await _retriever.SearchAsync(coleccion, consulta, topK, perfil, cancellationToken);
            return new BusquedaResponse(coleccion,
                hits.Select(h => new ResultadoBusquedaResponse(h.ChunkId, h.Score, h.RelativePath, h.Content)).ToArray());
        });

    public Task<IngestaResponse> IngerirAsync(string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var resultado = await _pipeline.IngestAsync(coleccion, perfil,
                documentos.Select(d => (d.Ruta!, d.Contenido!)).ToArray(), cancellationToken);
            return new IngestaResponse(Guid.NewGuid(), coleccion, "completada",
                resultado.Received, resultado.Indexed, resultado.Unchanged, resultado.Omitted);
        });

    private async Task<T> ConQdrantAsync<T>(Func<Task<T>> operacion)
    {
        try
        {
            return await operacion();
        }
        catch (RpcException ex) when (ex.StatusCode != StatusCode.Cancelled)
        {
            _logger.LogError(ex, "Fallo la llamada a Qdrant ({Status}).", ex.StatusCode);
            throw new MotorNoDisponibleException("El vector store no esta disponible o rechazo la operacion.");
        }
    }
}
