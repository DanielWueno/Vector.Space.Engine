using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;

/// <summary>
/// Adaptador Qdrant portado de Rag.Context.Engine (Infrastructure/VectorStore/QdrantVectorStore.cs): mismo esquema de
/// coleccion (vectores <c>dense</c> coseno y <c>sparse-code</c>, indice keyword de <c>defined_symbols</c>) y mismo
/// payload por chunk, para que un punto escrito aqui sea el que escribiria aquel pipeline.
///
/// Cambios del porte, todos por el contrato de 1.3 (documentos por HTTP, una coleccion por tenant):
/// sin vector <c>dense-resumen</c> ni <c>resumen_pending</c> (la fase de resumen LLM no se porta), sin <c>tenant</c> en el
/// payload (la coleccion ya es el tenant), sin manifiesto; se agrega <c>document_fingerprint</c> e indice keyword de
/// <c>file_path</c> para decidir por documento si cambio y para barrer sus puntos obsoletos sin recorrer la coleccion.
/// </summary>
public sealed class QdrantVectorStore
{
    public const string DenseVectorName = "dense";
    public const string SparseVectorName = "sparse-code";
    public const string DefinedSymbolsPayloadKey = "defined_symbols";
    public const string ConsumedSymbolsPayloadKey = "consumed_symbols";
    public const string FilePathPayloadKey = "file_path";

    /// <summary>Huella del documento con el que se generaron los puntos (ver DocumentIngestionPipeline).</summary>
    public const string DocumentFingerprintPayloadKey = "document_fingerprint";

    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(QdrantClient client, ILogger<QdrantVectorStore> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Crea la coleccion si no existe. Si existe, exige el vector denso nombrado con la dimension del modelo: mezclar
    /// vectores de dos modelos en una coleccion corrompe la busqueda sin error visible (p. ej. una coleccion MiniLM
    /// 384d de Rag.Context.Engine, que 1.6 manda reingestar).
    /// </summary>
    public async Task EnsureCollectionAsync(string collectionName, int dimension, CancellationToken ct)
    {
        var existing = await GetDenseDimensionAsync(collectionName, ct);
        if (existing is null)
        {
            await CreateCollectionAsync(collectionName, dimension, ct);
            return;
        }

        if (existing != (ulong)dimension)
            throw new ConflictException(
                $"La coleccion '{collectionName}' existe con vectores de dimension {existing}; el modelo del servicio usa {dimension}. " +
                "Hay que reingestarla en una coleccion nueva.");
    }

    /// <summary>Dimension del vector <c>dense</c>, 0 si la coleccion existe sin el, null si no existe.</summary>
    public async Task<ulong?> GetDenseDimensionAsync(string collectionName, CancellationToken ct)
    {
        if (!await _client.CollectionExistsAsync(collectionName, ct))
            return null;

        var info = await _client.GetCollectionInfoAsync(collectionName, ct);
        var vectors = info.Config?.Params?.VectorsConfig;
        return vectors?.ConfigCase == VectorsConfig.ConfigOneofCase.ParamsMap &&
               vectors.ParamsMap.Map.TryGetValue(DenseVectorName, out var dense)
            ? dense.Size
            : 0;
    }

    private async Task CreateCollectionAsync(string collectionName, int dimension, CancellationToken ct)
    {
        var vectorsConfig = new VectorParamsMap();
        vectorsConfig.Map[DenseVectorName] = new VectorParams { Size = (ulong)dimension, Distance = Distance.Cosine };

        var sparseConfig = new SparseVectorConfig();
        sparseConfig.Map[SparseVectorName] = new SparseVectorParams
        {
            Index = new SparseIndexConfig { FullScanThreshold = 5000 }
        };

        try
        {
            await _client.CreateCollectionAsync(collectionName, vectorsConfig: vectorsConfig, sparseVectorsConfig: sparseConfig,
                cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            // Otra ingesta la creo entre la comprobacion y aqui: se valida su esquema como si ya existiera.
            await EnsureCollectionAsync(collectionName, dimension, ct);
            return;
        }

        await _client.CreatePayloadIndexAsync(collectionName, DefinedSymbolsPayloadKey, PayloadSchemaType.Keyword, cancellationToken: ct);
        await _client.CreatePayloadIndexAsync(collectionName, FilePathPayloadKey, PayloadSchemaType.Keyword, cancellationToken: ct);

        _logger.LogInformation("Coleccion Qdrant '{Name}' creada: {Dim}D dense + sparse-code.", collectionName, dimension);
    }

    /// <summary>Puntos ya indexados de cada documento (clave = <c>file_path</c>), con la huella con que se escribieron.</summary>
    public async Task<IReadOnlyDictionary<string, List<(Guid Id, string? Fingerprint)>>> GetDocumentPointsAsync(
        string collectionName, IReadOnlyCollection<string> filePaths, CancellationToken ct)
    {
        var result = new Dictionary<string, List<(Guid, string?)>>(StringComparer.Ordinal);
        if (filePaths.Count == 0) return result;

        var filter = new Filter { Must = { Conditions.Match(FilePathPayloadKey, filePaths.ToList()) } };
        PointId? offset = null;
        while (true)
        {
            var response = await _client.ScrollAsync(collectionName, filter, limit: 1024, offset: offset,
                payloadSelector: new WithPayloadSelector
                {
                    Include = new PayloadIncludeSelector { Fields = { FilePathPayloadKey, DocumentFingerprintPayloadKey } }
                },
                vectorsSelector: new WithVectorsSelector { Enable = false },
                cancellationToken: ct);

            foreach (var point in response.Result)
            {
                var filePath = point.Payload[FilePathPayloadKey].StringValue;
                var fingerprint = point.Payload.TryGetValue(DocumentFingerprintPayloadKey, out var f) ? f.StringValue : null;
                if (!result.TryGetValue(filePath, out var list))
                    result[filePath] = list = [];
                list.Add((Guid.Parse(point.Id.Uuid), fingerprint));
            }

            var next = response.NextPageOffset;
            if (next is null || next.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.None) break;
            offset = next;
        }
        return result;
    }

    /// <summary>
    /// Fase 1 de Rag.Context.Engine (UpsertBatchAsync) con el mismo payload, mas <c>document_fingerprint</c>. Espera a que
    /// Qdrant aplique el lote: una busqueda que llega despues de responder la ingesta debe ver sus puntos.
    /// </summary>
    public async Task UpsertBatchAsync(string collectionName, IReadOnlyList<VectorStoreBatchItem> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        var points = batch.Select(item =>
        {
            var point = new PointStruct { Id = new PointId { Uuid = item.Chunk.Id.ToString() } };

            var sparseValues = new float[item.SparseVector.Count];
            var sparseIndices = new uint[item.SparseVector.Count];
            for (var i = 0; i < item.SparseVector.Count; i++)
            {
                sparseIndices[i] = item.SparseVector[i].TermIndex;
                sparseValues[i] = item.SparseVector[i].Weight;
            }

            var namedVectors = new NamedVectors();
            namedVectors.Vectors[DenseVectorName] = item.DenseVector;
            namedVectors.Vectors[SparseVectorName] = (sparseValues, sparseIndices);
            point.Vectors = new Vectors { Vectors_ = namedVectors };

            point.Payload["content"]          = new Value { StringValue = item.Chunk.Content };
            point.Payload["enriched_content"] = new Value { StringValue = item.Chunk.EnrichedContent };
            point.Payload[FilePathPayloadKey] = new Value { StringValue = item.Chunk.Metadata.FilePath };
            point.Payload["relative_path"]    = new Value { StringValue = item.Chunk.Metadata.RelativeFilePath };
            point.Payload["language"]         = new Value { StringValue = item.Chunk.Metadata.Language.ToString() };
            point.Payload["start_line"]       = new Value { IntegerValue = item.Chunk.Metadata.StartLine };
            point.Payload["end_line"]         = new Value { IntegerValue = item.Chunk.Metadata.EndLine };
            point.Payload["chunk_type"]       = new Value { StringValue = item.Chunk.Type.ToString() };
            point.Payload["content_hash"]     = new Value { StringValue = item.Chunk.ContentHash };
            point.Payload["last_modified"]    = new Value { StringValue = item.Chunk.Metadata.LastModified.ToString("O") };
            point.Payload["repository_name"]  = new Value { StringValue = item.Chunk.Metadata.RepositoryName };
            point.Payload[DocumentFingerprintPayloadKey] = new Value { StringValue = item.DocumentFingerprint };

            if (item.Chunk.Metadata.Namespace is not null)
                point.Payload["namespace"] = new Value { StringValue = item.Chunk.Metadata.Namespace };
            if (item.Chunk.Metadata.ClassName is not null)
                point.Payload["class_name"] = new Value { StringValue = item.Chunk.Metadata.ClassName };
            if (item.Chunk.Metadata.MethodName is not null)
                point.Payload["method_name"] = new Value { StringValue = item.Chunk.Metadata.MethodName };

            // Igual que el origen: listas vacias se omiten (Markdown, Fallback o extraccion fallida).
            if (item.Chunk.DefinedSymbols.Count > 0)
                point.Payload[DefinedSymbolsPayloadKey] = ToKeywordListValue(item.Chunk.DefinedSymbols);
            if (item.Chunk.ConsumedSymbols.Count > 0)
                point.Payload[ConsumedSymbolsPayloadKey] = ToKeywordListValue(item.Chunk.ConsumedSymbols);

            return point;
        }).ToList();

        await _client.UpsertAsync(collectionName, points, wait: true, cancellationToken: ct);
    }

    public async Task DeletePointsAsync(string collectionName, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        const int DeleteBatch = 1024;
        for (var i = 0; i < ids.Count; i += DeleteBatch)
        {
            var slice = ids.Skip(i).Take(DeleteBatch).ToList();
            await _client.DeleteAsync(collectionName, slice, wait: true, cancellationToken: ct);
        }
    }

    private static Value ToKeywordListValue(IReadOnlyList<string> symbols)
    {
        var listValue = new ListValue();
        listValue.Values.AddRange(symbols.Select(s => new Value { StringValue = s }));
        return new Value { ListValue = listValue };
    }
}

/// <summary>Un chunk con sus vectores y la huella del documento que lo genero.</summary>
public sealed record VectorStoreBatchItem(
    CodeChunk Chunk,
    float[] DenseVector,
    IReadOnlyList<SparseEntry> SparseVector,
    string DocumentFingerprint);
