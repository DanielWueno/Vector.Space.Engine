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

    /// <summary>Vector de resumen de negocio, opt-in al crear la coleccion (1.7.7, nombre literal fijado en el ledger).</summary>
    public const string ResumenDenseVectorName = "dense-resumen";

    /// <summary>true mientras un punto (de una coleccion con resumen habilitado) no tiene su resumen generado todavia.</summary>
    public const string ResumenPendingPayloadKey = "resumen_pending";

    /// <summary>Modelo de LLM con el que se genero el resumen de este punto (distingue la cache si cambia el perfil).</summary>
    public const string ResumenModeloPayloadKey = "resumen_modelo";

    /// <summary>Version del prompt de resumen (hash de prompt+modelo) con la que se genero este punto.</summary>
    public const string ResumenPromptVersionPayloadKey = "resumen_prompt_version";

    /// <summary>
    /// Texto del resumen de negocio de este punto (item 1.20): solo en puntos con <c>dense-resumen</c> resumidos desde
    /// 1.20; los resumidos antes no lo tienen (el texto solo vivia en la cache en memoria) hasta volver a resumirse.
    /// </summary>
    public const string ResumenTextoPayloadKey = "resumen_texto";

    /// <summary>HttpClient REST nombrado (1.7.4): el cliente gRPC de Qdrant 1.19 no expone el campo <c>metadata</c> de la coleccion.</summary>
    public const string RestHttpClientName = "motor-qdrant-rest";

    private const string PerfilEmbeddingsMetadataKey = "perfil_embeddings";
    private const string ModeloMetadataKey = "modelo";
    private const string DimensionMetadataKey = "dimension";
    private const string ResumenHabilitadoMetadataKey = "resumen_habilitado";

    /// <summary>Perfil de embeddings con el que se crea <c>dense-resumen</c> (item 1.20); ausente en colecciones anteriores.</summary>
    private const string PerfilResumenMetadataKey = "perfil_resumen";

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
    /// Crea la coleccion si no existe (con la metadata de su perfil de embeddings, y el vector <c>dense-resumen</c> si
    /// <paramref name="resumenSolicitado"/>, 1.7.7) o, si ya existe, verifica que la dimension y el perfil coincidan
    /// (1.7.4): mezclar vectores de dos modelos en una coleccion corrompe la busqueda sin error visible (p. ej. una
    /// coleccion MiniLM 384d de Rag.Context.Engine, que 1.6 manda reingestar; o bge-m3 truncado a 256 entrando en una
    /// coleccion arctic@256, que 1.7.3 midio con 200 y documentos inalcanzables).
    /// </summary>
    /// <param name="resumenSolicitado">
    /// 1.7.7: pedir resumen explicitamente. Sobre una coleccion que YA existe sin el vector de resumen, lanza
    /// <see cref="ConflictException"/> (409): el tercer vector no se puede agregar despues de crear la coleccion
    /// (1.7.3). Sobre una coleccion nueva, la crea con <c>dense-resumen</c> ademas de <c>dense</c>.
    /// </param>
    /// <param name="resumenPerfil">
    /// Nombre del perfil de embeddings del resumen; solo se usa al crear la coleccion, para guardarlo en su metadata
    /// (<c>perfil_resumen</c>, item 1.20) y que una busqueda en el espacio resumen detecte un cambio de modelo.
    /// </param>
    /// <param name="resumenDimension">Dimension del vector de resumen; solo se usa al crear la coleccion.</param>
    /// <returns>Si la coleccion (ya existente o recien creada) tiene el resumen habilitado de verdad (vector presente).</returns>
    public async Task<bool> EnsureCollectionAsync(
        string collectionName, string perfil, string modelo, int dimension,
        bool resumenSolicitado, string? resumenPerfil, int resumenDimension, CancellationToken ct)
    {
        var esquema = await GetEsquemaAsync(collectionName, ct);
        if (esquema is null)
        {
            await CreateCollectionAsync(collectionName, perfil, modelo, dimension, resumenSolicitado, resumenPerfil, resumenDimension, ct);
            await EscribirPerfilAsync(collectionName, perfil, modelo, dimension, resumenSolicitado, ct, resumenSolicitado ? resumenPerfil : null);
            return resumenSolicitado;
        }

        // El vector de resumen es del esquema real de la coleccion en Qdrant, no de la metadata (que es solo
        // informativa): asi un 409 nunca depende de que alguien haya escrito la metadata correctamente antes.
        if (resumenSolicitado && !esquema.Value.TieneResumen)
            throw new ConflictException(
                $"La coleccion '{collectionName}' no tiene el vector de resumen de negocio ('{ResumenDenseVectorName}'); " +
                "no se puede activar el resumen sobre una coleccion existente. Hay que reingestarla en una coleccion nueva con resumen habilitado.");

        await VerificarPerfilExistenteAsync(collectionName, esquema.Value.Dense, perfil, modelo, dimension, esquema.Value.TieneResumen, ct, puedeEscribirMetadata: true);
        return esquema.Value.TieneResumen;
    }

    /// <summary>
    /// Igual que <see cref="EnsureCollectionAsync"/> pero sin crear la coleccion (busquedas y calculo de embeddings,
    /// 1.7.4): si la coleccion no existe todavia no hay nada que verificar (no es un error, ver
    /// <c>QdrantSemanticRetriever</c>); si existe, un perfil distinto responde 409 sin escribir.
    /// </summary>
    public async Task VerificarPerfilParaConsultaAsync(string collectionName, string perfil, string modelo, int dimension, CancellationToken ct)
    {
        var esquema = await GetEsquemaAsync(collectionName, ct);
        if (esquema is null)
            return;

        await VerificarPerfilExistenteAsync(collectionName, esquema.Value.Dense, perfil, modelo, dimension, esquema.Value.TieneResumen, ct, puedeEscribirMetadata: false);
    }

    /// <summary>
    /// Busqueda en el espacio resumen (item 1.20), sin escribir nada: false si la coleccion no existe (no es un error,
    /// no tiene resultados). Si existe, 409 (<see cref="ConflictException"/>) cuando su esquema real en Qdrant no
    /// tiene <c>dense-resumen</c> (no la metadata, igual criterio que <see cref="EnsureCollectionAsync"/>), cuando la
    /// dimension de ese vector no es la del perfil de resumen de la instancia, o cuando la coleccion guardo otro
    /// <c>perfil_resumen</c> al crearse (misma dimension, otro modelo: el hueco que 1.7.4 cerro para <c>dense</c>).
    /// Una coleccion creada antes de 1.20 no tiene <c>perfil_resumen</c>: solo se comprueba la dimension.
    /// </summary>
    public async Task<bool> VerificarEsquemaResumenParaConsultaAsync(
        string collectionName, string perfilResumen, string modeloResumen, int dimensionResumen, CancellationToken ct)
    {
        var esquema = await GetEsquemaAsync(collectionName, ct);
        if (esquema is null)
            return false;

        if (!esquema.Value.TieneResumen)
            throw new ConflictException(
                $"La coleccion '{collectionName}' no tiene el vector de resumen de negocio ('{ResumenDenseVectorName}'); " +
                "no se puede buscar en el espacio 'resumen'. Solo las colecciones creadas con resumen habilitado lo tienen.");

        if (esquema.Value.ResumenDimension != (ulong)dimensionResumen)
            throw new ConflictException(
                $"La coleccion '{collectionName}' tiene el vector '{ResumenDenseVectorName}' de dimension {esquema.Value.ResumenDimension}; " +
                $"el perfil de resumen del servicio ('{perfilResumen}', {modeloResumen}) usa {dimensionResumen}.");

        var almacenado = await LeerPerfilAsync(collectionName, ct);
        if (almacenado?.PerfilResumen is { } guardado && !string.Equals(guardado, perfilResumen, StringComparison.Ordinal))
            throw new ConflictException(
                $"La coleccion '{collectionName}' genero '{ResumenDenseVectorName}' con el perfil de embeddings '{guardado}'; " +
                $"el perfil de resumen del servicio es '{perfilResumen}' ({modeloResumen}). Aunque la dimension coincida ({dimensionResumen}), " +
                "los vectores no son comparables.");

        return true;
    }

    private async Task VerificarPerfilExistenteAsync(
        string collectionName, ulong existingDimension, string perfil, string modelo, int dimension,
        bool resumenReal, CancellationToken ct, bool puedeEscribirMetadata)
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
                await EscribirPerfilAsync(collectionName, perfil, modelo, dimension, resumenReal, ct);
            return;
        }

        if (!string.Equals(almacenado.Perfil, perfil, StringComparison.Ordinal))
            throw new ConflictException(
                $"La coleccion '{collectionName}' se creo con el perfil de embeddings '{almacenado.Perfil}' ({almacenado.Modelo}); " +
                $"la solicitud usa el perfil '{perfil}' ({modelo}). Aunque la dimension coincida ({dimension}), " +
                "hay que reingestarla en una coleccion nueva para cambiar de perfil.");
    }

    /// <summary>
    /// Nombre de cada coleccion existente en Qdrant, con su metadata de perfil si la tiene (item 1.16). El listado en
    /// si es gRPC (<c>ListCollectionsAsync</c>, lo cubre <c>ConQdrantAsync</c> del llamador); leer el perfil de cada
    /// una es REST, una llamada por coleccion (mismo costo que <see cref="LeerPerfilAsync"/> en cualquier otro lado),
    /// aceptable para un catalogo de descubrimiento, no para una ruta caliente.
    /// </summary>
    public async Task<IReadOnlyList<(string Nombre, PerfilColeccion? Perfil)>> ListarColeccionesAsync(CancellationToken ct)
    {
        var nombres = await _client.ListCollectionsAsync(ct);
        var resultado = new List<(string, PerfilColeccion?)>(nombres.Count);
        foreach (var nombre in nombres)
            resultado.Add((nombre, await LeerPerfilAsync(nombre, ct)));
        return resultado;
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
            var resumenHabilitado = metadata.TryGetProperty(ResumenHabilitadoMetadataKey, out var resumenValue) &&
                                     resumenValue.ValueKind == JsonValueKind.True;
            var perfilResumen = metadata.TryGetProperty(PerfilResumenMetadataKey, out var perfilResumenValue) &&
                                perfilResumenValue.ValueKind == JsonValueKind.String
                ? perfilResumenValue.GetString()
                : null;

            return new PerfilColeccion(perfilValue.GetString()!, modelo, dimension, resumenHabilitado, perfilResumen);
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

    private async Task EscribirPerfilAsync(
        string collectionName, string perfil, string modelo, int dimension, bool resumenHabilitado, CancellationToken ct,
        string? perfilResumen = null)
    {
        try
        {
            var metadata = new Dictionary<string, object>
            {
                [PerfilEmbeddingsMetadataKey] = perfil,
                [ModeloMetadataKey] = modelo,
                [DimensionMetadataKey] = dimension,
                [ResumenHabilitadoMetadataKey] = resumenHabilitado,
            };
            // 1.20: solo al crear una coleccion con resumen; sin resumen el campo no se escribe (mismo esquema de antes).
            if (perfilResumen is not null)
                metadata[PerfilResumenMetadataKey] = perfilResumen;
            var cuerpo = new { metadata };
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

    /// <summary>
    /// Dimension del vector <c>dense</c> (0 si la coleccion existe sin el), si ya tiene <c>dense-resumen</c> y su
    /// dimension (0 si no lo tiene, item 1.20); null si la coleccion no existe.
    /// </summary>
    private async Task<(ulong Dense, bool TieneResumen, ulong ResumenDimension)?> GetEsquemaAsync(string collectionName, CancellationToken ct)
    {
        if (!await _client.CollectionExistsAsync(collectionName, ct))
            return null;

        var info = await _client.GetCollectionInfoAsync(collectionName, ct);
        var vectors = info.Config?.Params?.VectorsConfig;
        if (vectors?.ConfigCase != VectorsConfig.ConfigOneofCase.ParamsMap)
            return (0, false, 0);

        var dense = vectors.ParamsMap.Map.TryGetValue(DenseVectorName, out var d) ? d.Size : 0;
        var tieneResumen = vectors.ParamsMap.Map.TryGetValue(ResumenDenseVectorName, out var r);
        return (dense, tieneResumen, tieneResumen ? r!.Size : 0);
    }

    private async Task CreateCollectionAsync(
        string collectionName, string perfil, string modelo, int dimension, bool resumenHabilitado, string? resumenPerfil, int resumenDimension,
        CancellationToken ct)
    {
        var vectorsConfig = new VectorParamsMap();
        vectorsConfig.Map[DenseVectorName] = new VectorParams { Size = (ulong)dimension, Distance = Distance.Cosine };
        if (resumenHabilitado)
            vectorsConfig.Map[ResumenDenseVectorName] = new VectorParams { Size = (ulong)resumenDimension, Distance = Distance.Cosine };

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
            await EnsureCollectionAsync(collectionName, perfil, modelo, dimension, resumenHabilitado, resumenPerfil, resumenDimension, ct);
            return;
        }

        await _client.CreatePayloadIndexAsync(collectionName, DefinedSymbolsPayloadKey, PayloadSchemaType.Keyword, cancellationToken: ct);
        await _client.CreatePayloadIndexAsync(collectionName, FilePathPayloadKey, PayloadSchemaType.Keyword, cancellationToken: ct);

        _logger.LogInformation("Coleccion Qdrant '{Name}' creada: {Dim}D dense + sparse-code{Resumen}.",
            collectionName, dimension, resumenHabilitado ? $" + {resumenDimension}D {ResumenDenseVectorName}" : "");
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

            // 1.7.7: solo en colecciones con resumen habilitado; una coleccion sin resumen no lleva este campo,
            // igual esquema que antes de este item.
            if (item.ResumenPendiente)
                point.Payload[ResumenPendingPayloadKey] = new Value { BoolValue = true };

            return point;
        }).ToList();

        await _client.UpsertAsync(collectionName, points, wait: true, cancellationToken: ct);
    }

    /// <summary>
    /// Recorre toda la coleccion sin filtro (scroll paginado por Id de punto, 1024 por pagina), trayendo solo los
    /// campos de payload pedidos. Privado: comparte la mecanica de paginacion entre <see cref="ListarRutasDocumentadasAsync"/>
    /// (1.7.5) y <see cref="ListarDocumentosConHuellaAsync"/> (1.19) sin que ninguno de los dos cambie su firma ni su
    /// semantica (en particular, no toca el barrido de documentos ausentes del origen 'proyecto', que depende del
    /// primero). No valida si la coleccion existe: eso lo decide cada llamador.
    /// </summary>
    private async Task<List<RetrievedPoint>> ScrollAllAsync(string collectionName, IReadOnlyList<string> payloadFields, CancellationToken ct)
    {
        var include = new PayloadIncludeSelector();
        include.Fields.AddRange(payloadFields);

        var points = new List<RetrievedPoint>();
        PointId? offset = null;
        while (true)
        {
            var response = await _client.ScrollAsync(collectionName, limit: 1024, offset: offset,
                payloadSelector: new WithPayloadSelector { Include = include },
                vectorsSelector: new WithVectorsSelector { Enable = false },
                cancellationToken: ct);

            points.AddRange(response.Result);

            var next = response.NextPageOffset;
            if (next is null || next.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.None) break;
            offset = next;
        }
        return points;
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

        foreach (var point in await ScrollAllAsync(collectionName, [FilePathPayloadKey], ct))
        {
            var filePath = point.Payload[FilePathPayloadKey].StringValue;
            if (!result.TryGetValue(filePath, out var list))
                result[filePath] = list = [];
            list.Add(Guid.Parse(point.Id.Uuid));
        }
        return result;
    }

    /// <summary>
    /// Cada documento indexado en la coleccion (clave = <c>file_path</c>), con la huella y el Id de cada uno de sus
    /// puntos (item 1.19): base del listado de solo lectura de documentos por coleccion (ruta, huella, cantidad de
    /// puntos), sin acceso directo a Qdrant. Mismo scroll sin filtro que <see cref="ListarRutasDocumentadasAsync"/>
    /// (comparten <see cref="ScrollAllAsync"/>), con <see cref="DocumentFingerprintPayloadKey"/> ademas de
    /// <see cref="FilePathPayloadKey"/>. Coleccion inexistente: diccionario vacio.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, List<(Guid Id, string? Fingerprint)>>> ListarDocumentosConHuellaAsync(
        string collectionName, CancellationToken ct)
    {
        var result = new Dictionary<string, List<(Guid, string?)>>(StringComparer.Ordinal);
        if (!await _client.CollectionExistsAsync(collectionName, ct))
            return result;

        foreach (var point in await ScrollAllAsync(collectionName, [FilePathPayloadKey, DocumentFingerprintPayloadKey], ct))
        {
            var filePath = point.Payload[FilePathPayloadKey].StringValue;
            var fingerprint = point.Payload.TryGetValue(DocumentFingerprintPayloadKey, out var f) ? f.StringValue : null;
            if (!result.TryGetValue(filePath, out var list))
                result[filePath] = list = [];
            list.Add((Guid.Parse(point.Id.Uuid), fingerprint));
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

    /// <summary>
    /// Una pagina de puntos con <c>resumen_pending=true</c> (1.7.7): fase 2 reanudable, consumida por
    /// <c>ResumenPipeline</c>. Coleccion inexistente: pagina vacia sin siguiente offset.
    /// </summary>
    public async Task<PaginaPendientesDeResumen> ListarPendientesDeResumenAsync(
        string collectionName, int limite, PointId? offset, CancellationToken ct)
    {
        if (!await _client.CollectionExistsAsync(collectionName, ct))
            return new PaginaPendientesDeResumen([], null);

        var filter = new Filter { Must = { Conditions.Match(ResumenPendingPayloadKey, true) } };
        var response = await _client.ScrollAsync(collectionName, filter, limit: (uint)limite, offset: offset,
            payloadSelector: new WithPayloadSelector
            {
                Include = new PayloadIncludeSelector { Fields = { "content", "language", "relative_path", "content_hash" } }
            },
            vectorsSelector: new WithVectorsSelector { Enable = false },
            cancellationToken: ct);

        var puntos = response.Result.Select(p => new PendienteDeResumen(
            Guid.Parse(p.Id.Uuid),
            p.Payload.TryGetValue("content", out var c) ? c.StringValue : "",
            p.Payload.TryGetValue("language", out var l) ? l.StringValue : "",
            p.Payload.TryGetValue("relative_path", out var r) ? r.StringValue : "",
            p.Payload.TryGetValue("content_hash", out var h) ? h.StringValue : "")).ToList();

        var next = response.NextPageOffset;
        var siguiente = next is null || next.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.None ? null : next;
        return new PaginaPendientesDeResumen(puntos, siguiente);
    }

    /// <summary>Cuantos puntos de la coleccion siguen con <c>resumen_pending=true</c> (1.7.7); 0 si no existe.</summary>
    public async Task<ulong> ContarPendientesDeResumenAsync(string collectionName, CancellationToken ct)
    {
        if (!await _client.CollectionExistsAsync(collectionName, ct))
            return 0;

        var filter = new Filter { Must = { Conditions.Match(ResumenPendingPayloadKey, true) } };
        return await _client.CountAsync(collectionName, filter, cancellationToken: ct);
    }

    /// <summary>
    /// Guarda el resumen generado para un punto (1.7.7): escribe <c>dense-resumen</c> solo si
    /// <paramref name="vectorResumen"/> no es null (un chunk "sin contenido de negocio" no lleva vector, igual que el
    /// origen), y siempre apaga <c>resumen_pending</c> y deja constancia del modelo y la version de prompt usados
    /// (para que la cache distinga resumenes de perfiles/prompts distintos). <paramref name="textoResumen"/> (item
    /// 1.20) se guarda en <c>resumen_texto</c> solo junto con el vector: un chunk sin contenido de negocio no lleva
    /// ni vector ni texto (el sentinel no es un resumen).
    /// </summary>
    public async Task ActualizarResumenAsync(
        string collectionName, Guid id, float[]? vectorResumen, string? textoResumen, string modeloLlm, string promptVersion, CancellationToken ct)
    {
        var pointId = new PointId { Uuid = id.ToString() };

        if (vectorResumen is not null)
        {
            var namedVectors = new NamedVectors();
            namedVectors.Vectors[ResumenDenseVectorName] = vectorResumen;
            await _client.UpdateVectorsAsync(collectionName,
                [new PointVectors { Id = pointId, Vectors = new Vectors { Vectors_ = namedVectors } }],
                wait: true, cancellationToken: ct);
        }

        var payload = new Dictionary<string, Value>
        {
            [ResumenPendingPayloadKey] = new Value { BoolValue = false },
            [ResumenModeloPayloadKey] = new Value { StringValue = modeloLlm },
            [ResumenPromptVersionPayloadKey] = new Value { StringValue = promptVersion },
        };
        if (vectorResumen is not null && textoResumen is not null)
            payload[ResumenTextoPayloadKey] = new Value { StringValue = textoResumen };
        await _client.SetPayloadAsync(collectionName, payload, new List<Guid> { id }, wait: true, cancellationToken: ct);
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
    string DocumentFingerprint,
    bool ResumenPendiente = false);

/// <summary>
/// Perfil de embeddings guardado en la metadata de una coleccion de Qdrant (1.7.4; <see cref="ResumenHabilitado"/>
/// desde 1.7.7; <see cref="PerfilResumen"/> desde 1.20, null en colecciones creadas antes o sin resumen).
/// </summary>
public sealed record PerfilColeccion(string Perfil, string Modelo, int Dimension, bool ResumenHabilitado = false, string? PerfilResumen = null);

/// <summary>Una pagina de <see cref="QdrantVectorStore.ListarPendientesDeResumenAsync"/> (1.7.7).</summary>
public sealed record PaginaPendientesDeResumen(IReadOnlyList<PendienteDeResumen> Puntos, PointId? SiguienteOffset);

/// <summary>Un punto pendiente de resumen de negocio, con lo que necesita el prompt (1.7.7).</summary>
public sealed record PendienteDeResumen(Guid Id, string Contenido, string Lenguaje, string RutaRelativa, string ContentHash);
