using Grpc.Core;
using Microsoft.Extensions.Logging;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Ingestion;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure;

/// <summary>
/// Implementa los puertos del contrato (<see cref="IMotorDeEmbeddings"/>, <see cref="IMotorDeBusqueda"/>,
/// <see cref="IMotorDeIngesta"/>) con el motor portado. Recibe la coleccion ya autorizada. Los fallos de Qdrant se
/// traducen a 503 sin exponer al cliente el detalle de la conexion.
/// </summary>
public sealed class MotorVectorial : IMotorDeEmbeddings, IMotorDeBusqueda, IMotorDeIngesta
{
    private readonly IVectorizationBrain _brain;
    private readonly QdrantSemanticRetriever _retriever;
    private readonly DocumentIngestionPipeline _pipeline;
    private readonly ILogger<MotorVectorial> _logger;

    public MotorVectorial(
        IVectorizationBrain brain,
        QdrantSemanticRetriever retriever,
        DocumentIngestionPipeline pipeline,
        ILogger<MotorVectorial> logger)
    {
        _brain = brain;
        _retriever = retriever;
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <summary>Vectores de documento (sin prefijo de consulta): los mismos que se guardarian al ingestar esos textos.</summary>
    public async Task<EmbeddingsResponse> CalcularAsync(string coleccion, IReadOnlyList<string> textos, CancellationToken cancellationToken)
    {
        var vectores = await _brain.EmbedDocumentsAsync(textos, cancellationToken);
        return new EmbeddingsResponse(coleccion, _brain.ModelName, _brain.EmbeddingDimensions, vectores);
    }

    public Task<BusquedaResponse> BuscarAsync(string coleccion, string consulta, int topK, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var hits = await _retriever.SearchAsync(coleccion, consulta, topK, cancellationToken);
            return new BusquedaResponse(coleccion,
                hits.Select(h => new ResultadoBusquedaResponse(h.ChunkId, h.Score, h.RelativePath, h.Content)).ToArray());
        });

    public Task<IngestaResponse> IngerirAsync(string coleccion, IReadOnlyList<DocumentoRequest> documentos, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var resultado = await _pipeline.IngestAsync(coleccion,
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
