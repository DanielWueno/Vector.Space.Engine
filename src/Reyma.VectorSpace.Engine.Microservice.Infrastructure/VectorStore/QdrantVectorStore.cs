using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business;
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

    /// <summary>HttpClient REST nombrado (1.7.4): el cliente gRPC de Qdrant 1.19 no expone el campo <c>metadata</c> de la coleccion.</summary>
    public const string RestHttpClientName = "motor-qdrant-rest";

    private const string PerfilEmbeddingsMetadataKey = "perfil_embeddings";
    private const string ModeloMetadataKey = "modelo";
    private const string DimensionMetadataKey = "dimension";

    private readonly QdrantClient _client;
    private readonly HttpClient _rest;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(QdrantClient client, HttpClient rest, ILogger<QdrantVectorStore> logger)
    {
        _client = client;
        _rest = rest;
        _logger = logger;
    }

    /// <summary>
    /// Crea la coleccion si no existe (con la metadata de su perfil de embeddings) o, si ya existe, verifica que la
    /// dimension y el perfil coincidan (1.7.4): mezclar vectores de dos modelos en una coleccion corrompe la busqueda
    /// sin error visible (p. ej. una coleccion MiniLM 384d de Rag.Context.Engine, que 1.6 manda reingestar; o bge-m3
    /// truncado a 256 entrando en una coleccion arctic@256, que 1.7.3 midio con 200 y documentos inalcanzables).
    /// </summary>
    public async Task EnsureCollectionAsync(string collectionName, string perfil, string modelo, int dimension, CancellationToken ct)
    {
        var existing = await GetDenseDimensionAsync(collectionName, ct);
        if (existing is null)
        {
            await CreateCollectionAsync(collectionName, perfil, modelo, dimension, ct);
            await EscribirPerfilAsync(collectionName, perfil, modelo, dimension, ct);
            return;
        }

        await VerificarPerfilExistenteAsync(collectionName, existing.Value, perfil, modelo, dimension, ct, puedeEscribirMetadata: true);
    }

    /// <summary>
    /// Igual que <see cref="EnsureCollectionAsync"/> pero sin crear la coleccion (busquedas y calculo de embeddings,
    /// 1.7.4): si la coleccion no existe todavia no hay nada que verificar (no es un error, ver
    /// <c>QdrantSemanticRetriever</c>); si existe, un perfil distinto responde 409 sin escribir.
    /// </summary>
    public async Task VerificarPerfilParaConsultaAsync(string collectionName, string perfil, string modelo, int dimension, CancellationToken ct)
    {
        var existing = await GetDenseDimensionAsync(collectionName, ct);
        if (existing is null)
            return;

        await VerificarPerfilExistenteAsync(collectionName, existing.Value, perfil, modelo, dimension, ct, puedeEscribirMetadata: false);
    }

    private async Task VerificarPerfilExistenteAsync(
        string collectionName, ulong existingDimension, string perfil, string modelo, int dimension, CancellationToken ct, bool puedeEscribirMetadata)
    {
        if (existingDimension != (ulong)dimension)
            throw new ConflictException(
                $"La coleccion '{collectionName}' existe con vectores de dimension {existingDimension}; el modelo del servicio usa {dimension}. " +
                "Hay que reingestarla en una coleccion nueva.");

        var almacenado = await LeerPerfilAsync(collectionName, ct);
        if (almacenado is null)
        {
            // Coleccion creada antes de 1.7.4 (sin metadata de perfil): se asume del perfil por defecto (1.6) y, si
            // coincide con lo que pide esta solicitud, se le escribe la metadata ahora (compatibilidad retroactiva).
            if (!string.Equals(perfil, MotorOptions.PerfilEmbeddingsPorDefecto, StringComparison.Ordinal))
                throw new ConflictException(
                    $"La coleccion '{collectionName}' no tiene perfil de embeddings registrado; se asume el perfil por defecto " +
                    $"'{MotorOptions.PerfilEmbeddingsPorDefecto}', no '{perfil}'. Hay que reingestarla en una coleccion nueva para cambiar de perfil.");

            if (puedeEscribirMetadata)
                await EscribirPerfilAsync(collectionName, perfil, modelo, dimension, ct);
            return;
        }

        if (!string.Equals(almacenado.Perfil, perfil, StringComparison.Ordinal))
            throw new ConflictException(
                $"La coleccion '{collectionName}' se creo con el perfil de embeddings '{almacenado.Perfil}' ({almacenado.Modelo}); " +
                $"la solicitud usa el perfil '{perfil}' ({modelo}). Aunque la dimension coincida ({dimension}), " +
                "hay que reingestarla en una coleccion nueva para cambiar de perfil.");
    }

    /// <summary>Perfil de embeddings guardado en la metadata de la coleccion (REST), o null si no la tiene (1.7.4).</summary>
    public async Task<PerfilColeccion?> LeerPerfilAsync(string collectionName, CancellationToken ct)
    {
        try
        {
            using var response = await _rest.GetAsync($"collections/{Uri.EscapeDataString(collectionName)}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            using var cuerpo = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (cuerpo is null ||
                !cuerpo.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("config", out var config) ||
                !config.TryGetProperty("metadata", out var metadata) ||
                metadata.ValueKind != JsonValueKind.Object)
                return null;

            if (!metadata.TryGetProperty(PerfilEmbeddingsMetadataKey, out var perfilValue) || perfilValue.ValueKind != JsonValueKind.String)
                return null;

            var modelo = metadata.TryGetProperty(ModeloMetadataKey, out var modeloValue) && modeloValue.ValueKind == JsonValueKind.String
                ? modeloValue.GetString() ?? ""
                : "";
            var dimension = metadata.TryGetProperty(DimensionMetadataKey, out var dimensionValue) && dimensionValue.ValueKind == JsonValueKind.Number
                ? dimensionValue.GetInt32()
                : 0;

            return new PerfilColeccion(perfilValue.GetString()!, modelo, dimension);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "No se pudo leer la metadata de perfil de la coleccion '{Collection}'.", collectionName);
            throw new MotorNoDisponibleException("El vector store no esta disponible o rechazo la operacion.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Leer la metadata de perfil de la coleccion '{Collection}' no respondio a tiempo.", collectionName);
            throw new MotorNoDisponibleException("El vector store no esta disponible o rechazo la operacion.");
        }
    }

    private async Task EscribirPerfilAsync(string collectionName, string perfil, string modelo, int dimension, CancellationToken ct)
    {
        try
        {
            var cuerpo = new { metadata = new { perfil_embeddings = perfil, modelo, dimension } };
            using var response = await _rest.PatchAsJsonAsync($"collections/{Uri.EscapeDataString(collectionName)}", cuerpo, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detalle = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Qdrant respondio {Status} al guardar la metadata de perfil de '{Collection}': {Detalle}",
                    (int)response.StatusCode, collectionName, detalle);
                throw new MotorNoDisponibleException("El vector store no esta disponible o rechazo la operacion.");
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "No se pudo guardar la metadata de perfil de la coleccion '{Collection}'.", collectionName);
            throw new MotorNoDisponibleException("El vector store no esta disponible o rechazo la operacion.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Guardar la metadata de perfil de la coleccion '{Collection}' no respondio a tiempo.", collectionName);
            throw new MotorNoDisponibleException("El vector store no esta disponible o rechazo la operacion.");
        }
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

    private async Task CreateCollectionAsync(string collectionName, string perfil, string modelo, int dimension, CancellationToken ct)
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
            // Otra ingesta la creo entre la comprobacion y aqui: se valida su esquema y su perfil como si ya existiera.
            await EnsureCollectionAsync(collectionName, perfil, modelo, dimension, ct);
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

    /// <summary>
    /// Todas las rutas ya indexadas en la coleccion, con los Ids de sus puntos (1.7.5): sin filtro, para el barrido de
    /// documentos ausentes del origen 'proyecto' (el .zip conoce el arbol completo, a diferencia del origen
    /// 'documentos', que solo ve lo que el cliente decide enviar). Coleccion inexistente: diccionario vacio.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, List<Guid>>> ListarRutasDocumentadasAsync(string collectionName, CancellationToken ct)
    {
        var result = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        if (!await _client.CollectionExistsAsync(collectionName, ct))
            return result;

        PointId? offset = null;
        while (true)
        {
            var response = await _client.ScrollAsync(collectionName, limit: 1024, offset: offset,
                payloadSelector: new WithPayloadSelector
                {
                    Include = new PayloadIncludeSelector { Fields = { FilePathPayloadKey } }
                },
                vectorsSelector: new WithVectorsSelector { Enable = false },
                cancellationToken: ct);

            foreach (var point in response.Result)
            {
                var filePath = point.Payload[FilePathPayloadKey].StringValue;
                if (!result.TryGetValue(filePath, out var list))
                    result[filePath] = list = [];
                list.Add(Guid.Parse(point.Id.Uuid));
            }

            var next = response.NextPageOffset;
            if (next is null || next.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.None) break;
            offset = next;
        }
        return result;
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

/// <summary>Perfil de embeddings guardado en la metadata de una coleccion de Qdrant (1.7.4).</summary>
public sealed record PerfilColeccion(string Perfil, string Modelo, int Dimension);
