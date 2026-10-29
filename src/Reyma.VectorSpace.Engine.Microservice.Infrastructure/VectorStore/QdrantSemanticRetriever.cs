using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;

/// <summary>
/// Busqueda portada de Rag.Context.Engine (QdrantSemanticRetriever, rama sin vector de resumen). Ramas exactas con
/// prefijo completo de empates (<see cref="DeterministicVectorQuery"/>) y el mismo desempate por id.
///
/// Por defecto solo la rama densa (1.10: ninguna fusion gano preguntas al denso sin perder otras con arctic). La RRF
/// nativa denso + sparse-code se conserva tal cual detras de <see cref="ModoBusqueda.FusionNativa"/>.
///
/// No se portan: reranking cross-encoder, salto por simbolo (6.a), perfiles por coleccion (7.a) y filtros de
/// modulo/tenant/lenguaje; el contrato de 1.3 no los expone y la coleccion ya es el tenant. La banda resumen del origen
/// era una rama de una RRF ponderada de tres ramas; aqui (item 1.20) es un espacio de busqueda aparte que el cliente
/// elige explicitamente (<see cref="EspacioDeBusqueda.Resumen"/>), siempre solo densa y sin fusion con ninguna otra.
/// </summary>
public sealed class QdrantSemanticRetriever
{
    private readonly QdrantClient _client;
    private readonly IVectorizationBrainFactory _brainFactory;
    private readonly QdrantVectorStore _store;
    private readonly ISparseTokenizer _sparseTokenizer;
    private readonly BusquedaOptions _options;
    private readonly string? _perfilResumen;
    private readonly ILogger<QdrantSemanticRetriever> _logger;

    public QdrantSemanticRetriever(
        QdrantClient client,
        IVectorizationBrainFactory brainFactory,
        QdrantVectorStore store,
        ISparseTokenizer sparseTokenizer,
        IOptions<MotorOptions> options,
        ILogger<QdrantSemanticRetriever> logger)
    {
        _client = client;
        _brainFactory = brainFactory;
        _store = store;
        _sparseTokenizer = sparseTokenizer;
        _options = options.Value.Busqueda;
        _perfilResumen = options.Value.Resumen.PerfilEmbeddings;
        _logger = logger;
    }

    /// <summary>
    /// <paramref name="perfil"/> null = perfil por defecto (1.7.4). Una coleccion existente con otro perfil responde
    /// 409 sin buscar (mismo criterio que la ingesta, ver <see cref="QdrantVectorStore.VerificarPerfilParaConsultaAsync"/>).
    /// <paramref name="simbolosDefinidos"/> (item 1.18): filtro exacto opcional, no vacio = acota a puntos cuyo
    /// <c>defined_symbols</c> contenga alguno de esos nombres (indice keyword ya existente), igual en ambos modos de
    /// busqueda. Dato, no logica: ningun salto de simbolo ni reranking vive aqui (ver nota de clase).
    /// <paramref name="espacio"/> (item 1.20): con <see cref="EspacioDeBusqueda.Resumen"/>, <paramref name="perfil"/>
    /// se ignora (Business ya rechaza que venga) y la consulta va con el perfil de <c>Motor:Resumen:PerfilEmbeddings</c>.
    /// </summary>
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collectionName, string query, int topK, string? perfil, IReadOnlyList<string>? simbolosDefinidos,
        EspacioDeBusqueda espacio, CancellationToken ct)
    {
        // 1.20: sin perfil de resumen configurado es un 400 claro, antes de tocar Qdrant o el sidecar (mismo mensaje
        // y mismo criterio que pedir resumen en la ingesta, DocumentIngestionPipeline.IngestLockedAsync).
        if (espacio == EspacioDeBusqueda.Resumen && string.IsNullOrWhiteSpace(_perfilResumen))
            throw new BadRequestException(
                "La busqueda en el espacio 'resumen' no esta disponible: el resumen de negocio no esta configurado en este servicio (falta Motor:Resumen:PerfilEmbeddings).");

        // Una coleccion autorizada que todavia no se ingesto no es un error: no tiene resultados.
        if (!await _client.CollectionExistsAsync(collectionName, ct))
            return [];

        var payload = new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector
            {
                Fields =
                {
                    "content", "relative_path", "start_line", "end_line", "language", "chunk_type", "content_hash",
                    "namespace", "class_name", "method_name",
                    QdrantVectorStore.DefinedSymbolsPayloadKey, QdrantVectorStore.ConsumedSymbolsPayloadKey,
                    QdrantVectorStore.ResumenTextoPayloadKey
                }
            }
        };
        Filter? filtroSimbolos = simbolosDefinidos is { Count: > 0 }
            ? new Filter { Must = { Conditions.Match(QdrantVectorStore.DefinedSymbolsPayloadKey, simbolosDefinidos) } }
            : null;

        if (espacio == EspacioDeBusqueda.Resumen)
            return Mapear(await BuscarEnResumenAsync(collectionName, query, (ulong)topK, payload, filtroSimbolos, ct));

        var resuelto = _brainFactory.Resolver(perfil);
        await _store.VerificarPerfilParaConsultaAsync(collectionName, resuelto.Nombre, resuelto.Brain.ModelName, resuelto.Brain.EmbeddingDimensions, ct);

        var queryVector = await resuelto.Brain.EmbedQueryAsync(query, ct);
        float? threshold = _options.UmbralSimilitud > 0f ? _options.UmbralSimilitud : null;

        IReadOnlyList<ScoredPoint> ranked;
        if (_options.Modo == ModoBusqueda.Denso)
        {
            ranked = await QueryOrderedAsync(collectionName, queryVector, QdrantVectorStore.DenseVectorName,
                (ulong)topK, payload, filtroSimbolos, ct, threshold);
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
                prefetchLimit, payload, filtroSimbolos, ct, threshold);
            var sparse = QueryOrderedAsync(collectionName, (sparseValues, sparseIndices), QdrantVectorStore.SparseVectorName,
                prefetchLimit, payload, filtroSimbolos, ct);
            await Task.WhenAll(dense, sparse);
            ranked = FuseNativeRanks(await dense, await sparse, finalLimit);
        }

        return Mapear(ranked);
    }

    /// <summary>
    /// Espacio resumen (item 1.20): solo la rama densa sobre <c>dense-resumen</c>, en cualquier
    /// <see cref="ModoBusqueda"/> (no hay sparse del resumen con que fusionar). El esquema se verifica antes de
    /// llamar al sidecar, asi que un 409 no gasta una vectorizacion. Solo devuelve puntos que tienen ese vector: los
    /// "sin contenido de negocio" y los que siguen pendientes de resumen no lo tienen y nunca aparecen aqui.
    /// Umbral: <see cref="BusquedaOptions.UmbralSimilitudResumen"/>, 0 por defecto (sin umbral).
    /// </summary>
    private async Task<IReadOnlyList<ScoredPoint>> BuscarEnResumenAsync(
        string collectionName, string query, ulong topK, WithPayloadSelector payload, Filter? filter, CancellationToken ct)
    {
        var resuelto = _brainFactory.Resolver(_perfilResumen);
        if (!await _store.VerificarEsquemaResumenParaConsultaAsync(
                collectionName, resuelto.Nombre, resuelto.Brain.ModelName, resuelto.Brain.EmbeddingDimensions, ct))
            return []; // la coleccion se borro entre la comprobacion de arriba y esta

        var queryVector = await resuelto.Brain.EmbedQueryAsync(query, ct);
        float? threshold = _options.UmbralSimilitudResumen > 0f ? _options.UmbralSimilitudResumen : null;
        return await QueryOrderedAsync(collectionName, queryVector, QdrantVectorStore.ResumenDenseVectorName,
            topK, payload, filter, ct, threshold);
    }

    private static IReadOnlyList<SearchHit> Mapear(IReadOnlyList<ScoredPoint> ranked) =>
        ranked.Select(p => new SearchHit(
                DeterministicVectorQuery.ChunkId(p),
                p.Score,
                p.Payload.GetValueOrDefault("relative_path")?.StringValue ?? string.Empty,
                p.Payload.GetValueOrDefault("content")?.StringValue ?? string.Empty,
                p.Payload.TryGetValue("start_line", out var inicio) ? (int)inicio.IntegerValue : null,
                p.Payload.TryGetValue("end_line", out var fin) ? (int)fin.IntegerValue : null,
                p.Payload.GetValueOrDefault("language")?.StringValue,
                p.Payload.GetValueOrDefault("chunk_type")?.StringValue,
                p.Payload.GetValueOrDefault("content_hash")?.StringValue,
                p.Payload.GetValueOrDefault("namespace")?.StringValue,
                p.Payload.GetValueOrDefault("class_name")?.StringValue,
                p.Payload.GetValueOrDefault("method_name")?.StringValue,
                ExtraerSimbolos(p.Payload, QdrantVectorStore.DefinedSymbolsPayloadKey),
                ExtraerSimbolos(p.Payload, QdrantVectorStore.ConsumedSymbolsPayloadKey),
                p.Payload.GetValueOrDefault(QdrantVectorStore.ResumenTextoPayloadKey)?.StringValue))
            .ToArray();

    /// <summary>Lista de keywords del payload (defined/consumed_symbols); vacia (nunca null) si el punto no la tiene.</summary>
    private static IReadOnlyList<string> ExtraerSimbolos(MapField<string, Value> payload, string clave) =>
        payload.TryGetValue(clave, out var valor) && valor.KindCase == Value.KindOneofCase.ListValue
            ? valor.ListValue.Values.Select(v => v.StringValue).ToArray()
            : Array.Empty<string>();

    private Task<IReadOnlyList<ScoredPoint>> QueryOrderedAsync(
        string collection, Query query, string vector, ulong limit, WithPayloadSelector payload, Filter? filter, CancellationToken ct,
        float? threshold = null) =>
        DeterministicVectorQuery.SearchAsync(_client, collection, query, vector, filter, limit, payload, _logger, ct, threshold);

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

/// <summary>
/// Un resultado de busqueda: id del chunk, puntuacion (coseno en modo denso, RRF en fusion), ruta, texto y los
/// metadatos de chunk ya guardados en el payload (item 1.18). <see cref="LineaInicio"/>/<see cref="LineaFin"/>/
/// <see cref="Lenguaje"/>/<see cref="TipoChunk"/>/<see cref="ContentHash"/> los escribe la ingesta en todo punto
/// (<see cref="QdrantVectorStore.UpsertBatchAsync"/>); null solo en un punto de una ingesta anterior sin esos campos.
/// <see cref="Namespace"/>/<see cref="Clase"/>/<see cref="Metodo"/> son null cuando el chunk no los tiene (p. ej.
/// Markdown). <see cref="SimbolosDefinidos"/>/<see cref="SimbolosConsumidos"/> son listas vacias, nunca null, cuando
/// el payload las omite (listas vacias no se escriben, ver <see cref="QdrantVectorStore.UpsertBatchAsync"/>).
/// <see cref="Resumen"/> (item 1.20): <c>resumen_texto</c> del punto, null si no lo tiene.
/// </summary>
public sealed record SearchHit(
    string ChunkId, float Score, string RelativePath, string Content,
    int? LineaInicio, int? LineaFin, string? Lenguaje, string? TipoChunk, string? ContentHash,
    string? Namespace, string? Clase, string? Metodo,
    IReadOnlyList<string> SimbolosDefinidos, IReadOnlyList<string> SimbolosConsumidos,
    string? Resumen = null);
