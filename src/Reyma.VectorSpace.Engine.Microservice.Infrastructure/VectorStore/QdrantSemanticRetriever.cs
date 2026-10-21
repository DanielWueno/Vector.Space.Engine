using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;

/// <summary>
/// Busqueda portada de Rag.Context.Engine (QdrantSemanticRetriever, rama sin vector de resumen). Ramas exactas con
/// prefijo completo de empates (<see cref="DeterministicVectorQuery"/>) y el mismo desempate por id.
///
/// Por defecto solo la rama densa (1.10: ninguna fusion gano preguntas al denso sin perder otras con arctic). La RRF
/// nativa denso + sparse-code se conserva tal cual detras de <see cref="ModoBusqueda.FusionNativa"/>.
///
/// No se portan: reranking cross-encoder, salto por simbolo (6.a), perfiles por coleccion (7.a), banda resumen y
/// filtros de modulo/tenant/lenguaje; el contrato de 1.3 no los expone y la coleccion ya es el tenant.
/// </summary>
public sealed class QdrantSemanticRetriever
{
    private readonly QdrantClient _client;
    private readonly IVectorizationBrain _brain;
    private readonly ISparseTokenizer _sparseTokenizer;
    private readonly BusquedaOptions _options;
    private readonly ILogger<QdrantSemanticRetriever> _logger;

    public QdrantSemanticRetriever(
        QdrantClient client,
        IVectorizationBrain brain,
        ISparseTokenizer sparseTokenizer,
        IOptions<MotorOptions> options,
        ILogger<QdrantSemanticRetriever> logger)
    {
        _client = client;
        _brain = brain;
        _sparseTokenizer = sparseTokenizer;
        _options = options.Value.Busqueda;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string collectionName, string query, int topK, CancellationToken ct)
    {
        // Una coleccion autorizada que todavia no se ingesto no es un error: no tiene resultados.
        if (!await _client.CollectionExistsAsync(collectionName, ct))
            return [];

        var queryVector = await _brain.EmbedQueryAsync(query, ct);
        var payload = new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector { Fields = { "content", "relative_path" } }
        };
        float? threshold = _options.UmbralSimilitud > 0f ? _options.UmbralSimilitud : null;

        IReadOnlyList<ScoredPoint> ranked;
        if (_options.Modo == ModoBusqueda.Denso)
        {
            ranked = await QueryOrderedAsync(collectionName, queryVector, QdrantVectorStore.DenseVectorName,
                (ulong)topK, payload, ct, threshold);
        }
        else
        {
            // Pool de cada rama varias veces mas ancho que el corte final, como en el origen sin reranking.
            var finalLimit = (ulong)topK;
            var prefetchLimit = Math.Max(finalLimit * 4, 40);

            var sparseEntries = _sparseTokenizer.Tokenize(query);
            var sparseValues = new float[sparseEntries.Count];
            var sparseIndices = new uint[sparseEntries.Count];
            for (var i = 0; i < sparseEntries.Count; i++)
            {
                sparseIndices[i] = sparseEntries[i].TermIndex;
                sparseValues[i] = sparseEntries[i].Weight;
            }

            var dense = QueryOrderedAsync(collectionName, queryVector, QdrantVectorStore.DenseVectorName,
                prefetchLimit, payload, ct, threshold);
            var sparse = QueryOrderedAsync(collectionName, (sparseValues, sparseIndices), QdrantVectorStore.SparseVectorName,
                prefetchLimit, payload, ct);
            await Task.WhenAll(dense, sparse);
            ranked = FuseNativeRanks(await dense, await sparse, finalLimit);
        }

        return ranked.Select(p => new SearchHit(
                DeterministicVectorQuery.ChunkId(p),
                p.Score,
                p.Payload.GetValueOrDefault("relative_path")?.StringValue ?? string.Empty,
                p.Payload.GetValueOrDefault("content")?.StringValue ?? string.Empty))
            .ToArray();
    }

    private Task<IReadOnlyList<ScoredPoint>> QueryOrderedAsync(
        string collection, Query query, string vector, ulong limit, WithPayloadSelector payload, CancellationToken ct,
        float? threshold = null) =>
        DeterministicVectorQuery.SearchAsync(_client, collection, query, vector, filter: null, limit, payload, _logger, ct, threshold);

    // Copia de QdrantSemanticRetriever.FuseNativeRanks del origen.
    private static IReadOnlyList<ScoredPoint> FuseNativeRanks(
        IReadOnlyList<ScoredPoint> dense, IReadOnlyList<ScoredPoint> sparse, ulong limit)
    {
        // Qdrant RRF: f32 accumulation in branch order, k=2 with zero-based ranks.
        // Server-side prefetch cannot express a secondary UUID order before truncation.
        var points = new Dictionary<string, (ScoredPoint Point, float Score)>(StringComparer.Ordinal);
        foreach (var branch in new[] { dense, sparse })
        {
            for (var rank = 0; rank < branch.Count; rank++)
            {
                var point = branch[rank];
                var id = DeterministicVectorQuery.ChunkId(point);
                points.TryGetValue(id, out var previous);
                points[id] = (previous.Point ?? point, previous.Score + 1f / (rank + 2));
            }
        }
        return RankingOrder.Descending(points.Values, p => p.Score,
                p => DeterministicVectorQuery.ChunkId(p.Point))
            .Take((int)limit).Select(p =>
            {
                p.Point.Score = p.Score;
                return p.Point;
            }).ToArray();
    }
}

/// <summary>Un resultado de busqueda: id del chunk, puntuacion (coseno en modo denso, RRF en fusion), ruta y texto.</summary>
public sealed record SearchHit(string ChunkId, float Score, string RelativePath, string Content);
