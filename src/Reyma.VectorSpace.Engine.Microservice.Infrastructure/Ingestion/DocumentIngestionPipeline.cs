using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reyma.Dates.Abstractions;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Chunking;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Utilities;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Ingestion;

/// <summary>
/// Fase 1 del DefaultIngestionPipeline de Rag.Context.Engine sobre documentos recibidos por HTTP en vez de un escaneo
/// de disco: misma estrategia de chunking por lenguaje, mismo filtro de admision, mismos Id y content_hash, vector denso
/// y sparse-code del texto enriquecido, upsert por lotes y barrido de los puntos que el documento ya no genera.
///
/// El repositorio del encabezado y de la clave de identidad es la coleccion: dentro de una coleccion, la ruta
/// identifica al documento (contrato de 1.3).
///
/// Diferencias con el origen: no hay fase de resumen LLM, auditoria ni estado SQLite por corrida; un documento cuya
/// huella y conjunto de Ids ya estan indexados se cuenta "sin cambios" y no se vuelve a vectorizar; un fallo del sidecar
/// o de Qdrant aborta la solicitud (el origen lo registraba y seguia con el siguiente lote). Reintentar es idempotente.
/// </summary>
public sealed class DocumentIngestionPipeline
{
    // Igual que el origen: menos de 60 caracteres no se indexa (ver IsIndexable).
    private const int MinIndexableContentChars = 60;

    // Ingestion:IndexShortTypeDeclarations del origen: experimental y apagado, tambien en los bancos de 1.6/1.11.
    internal const bool IndexShortTypeDeclarations = false;

    private readonly ChunkingStrategyRouter _chunkRouter;
    private readonly IVectorizationBrainFactory _brainFactory;
    private readonly ISparseTokenizer _sparseTokenizer;
    private readonly QdrantVectorStore _store;
    private readonly IDateTime _clock;
    private readonly IngestaOptions _options;
    private readonly ResumenOptions _resumenOptions;
    private readonly ILogger<DocumentIngestionPipeline> _logger;

    // Dos ingestas sobre la misma coleccion se serializan: el barrido de obsoletos de una no debe ver a medias los
    // upserts de la otra. Solo dentro de este proceso; varias replicas necesitarian un bloqueo externo. El limite
    // global de ingestas en curso y en cola (1.7) vive en Business (LimiteDeIngestas), antes del motor.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _collectionLocks = new(StringComparer.Ordinal);

    public DocumentIngestionPipeline(
        ChunkingStrategyRouter chunkRouter,
        IVectorizationBrainFactory brainFactory,
        ISparseTokenizer sparseTokenizer,
        QdrantVectorStore store,
        IDateTime clock,
        IOptions<MotorOptions> options,
        ILogger<DocumentIngestionPipeline> logger)
    {
        _chunkRouter = chunkRouter;
        _brainFactory = brainFactory;
        _sparseTokenizer = sparseTokenizer;
        _store = store;
        _clock = clock;
        _options = options.Value.Ingesta;
        _resumenOptions = options.Value.Resumen;
        _logger = logger;
    }

    // Copia de DefaultIngestionPipeline.IsIndexable del origen.
    internal static bool IsIndexable(CodeChunk chunk, bool indexShortTypeDeclarations)
    {
        var contentLength = chunk.Content.AsSpan().Trim().Length;
        if (contentLength >= MinIndexableContentChars)
            return true;

        // Class también etiqueta grupos de campos; sólo se exime el chunk que
        // declara el propio tipo, no cualquiera que pertenezca a él.
        var typeName = chunk.Metadata.ClassName;
        return indexShortTypeDeclarations
            && contentLength > 0
            && chunk.Type is ChunkType.Class or ChunkType.Interface
            && !string.IsNullOrWhiteSpace(typeName)
            && chunk.DefinedSymbols.Contains(typeName, StringComparer.Ordinal);
    }

    /// <summary>Las mismas opciones que arma el CLI del origen, con el tamano de chunk de configuracion (1.11).</summary>
    internal ChunkingOptions ChunkingOptionsFor(string collectionName) => new()
    {
        MaxTokensPerChunk = _options.MaxTokensPorChunk,
        OverlapTokens = _options.TokensDeSolape,
        RepositoryName = collectionName,
        BatchSize = _options.LoteDeChunks
    };

    /// <param name="barrerDocumentosAusentes">
    /// 1.7.5 (origen 'proyecto'): tras indexar, borra los puntos de documentos que ya estaban en la coleccion pero no
    /// reaparecieron en <paramref name="documents"/>. Solo tiene sentido cuando <paramref name="documents"/> es el
    /// arbol completo del proyecto (el .zip lo conoce); el origen 'documentos' lo deja en false porque un cliente
    /// puede enviar un subconjunto sin que eso signifique que el resto se borro. Corre dentro del mismo
    /// <see cref="SemaphoreSlim"/> por coleccion que el resto del metodo, para no pisarse con otra ingesta concurrente.
    /// </param>
    /// <param name="resumenSolicitado">
    /// 1.7.7: opt-in del resumen de negocio. Solo tiene efecto real al CREAR la coleccion (agrega el vector
    /// <c>dense-resumen</c>); pedirlo sobre una coleccion existente que no lo tiene es un 409 claro (ver
    /// <c>QdrantVectorStore.EnsureCollectionAsync</c>). Sobre una coleccion que YA lo tiene, da igual el valor: el
    /// resumen sigue automaticamente para los documentos nuevos o cambiados (es un atributo de la coleccion, igual
    /// que el perfil de embeddings).
    /// </param>
    public async Task<IngestionResult> IngestAsync(
        string collectionName, string? perfil, IReadOnlyList<(string Path, string Content)> documents, CancellationToken ct,
        bool barrerDocumentosAusentes = false, bool resumenSolicitado = false)
    {
        var gate = _collectionLocks.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var resultado = await IngestLockedAsync(collectionName, perfil, documents, resumenSolicitado, ct);
            if (!barrerDocumentosAusentes)
                return resultado;

            // El file_path guardado en Qdrant es la clave de identidad compuesta (ChunkBuilder.BuildIdentityKey),
            // no la ruta cruda: compararlas tal cual aqui habria barrido todo lo que se acababa de escribir.
            var actuales = documents.Select(d => ChunkBuilder.BuildIdentityKey(collectionName, d.Path)).ToHashSet(StringComparer.Ordinal);
            var eliminados = await BarrerDocumentosAusentesAsync(collectionName, actuales, ct);
            return resultado with { DocumentsSweptAway = eliminados };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Puntos de documentos indexados que no estan en <paramref name="actuales"/>; devuelve cuantos documentos se barrieron.</summary>
    private async Task<int> BarrerDocumentosAusentesAsync(string collectionName, HashSet<string> actuales, CancellationToken ct)
    {
        var existentes = await _store.ListarRutasDocumentadasAsync(collectionName, ct);
        var ausentes = existentes.Where(kv => !actuales.Contains(kv.Key)).ToList();
        if (ausentes.Count == 0)
            return 0;

        var ids = ausentes.SelectMany(kv => kv.Value).ToList();
        await _store.DeletePointsAsync(collectionName, ids, ct);
        _logger.LogInformation(
            "Barrido en '{Collection}': {Documentos} documentos ausentes del proyecto, {Puntos} puntos borrados.",
            collectionName, ausentes.Count, ids.Count);
        return ausentes.Count;
    }

    private async Task<IngestionResult> IngestLockedAsync(
        string collectionName, string? perfilSolicitado, IReadOnlyList<(string Path, string Content)> documents,
        bool resumenSolicitado, CancellationToken ct)
    {
        // 1.7.4: se resuelve el perfil de embeddings antes de trocear, porque el fingerprint de cada documento
        // incluye el modelo y la dimension (si el perfil cambia, el documento se vuelve a vectorizar).
        var resuelto = _brainFactory.Resolver(perfilSolicitado);
        var brain = resuelto.Brain;

        // 1.7.7: si se pide resumen pero esta instancia no tiene perfil de embeddings de resumen configurado, es un
        // 400 claro (motor sin configurar), nunca una resolucion silenciosa al perfil de embeddings por defecto ni
        // un 500. Se resuelve ANTES de EnsureCollectionAsync porque crear la coleccion necesita su dimension.
        if (resumenSolicitado && string.IsNullOrWhiteSpace(_resumenOptions.PerfilEmbeddings))
            throw new BadRequestException("El resumen de negocio no esta configurado en este servicio (falta Motor:Resumen:PerfilEmbeddings).");
        var resumenEmbeddings = resumenSolicitado ? _brainFactory.Resolver(_resumenOptions.PerfilEmbeddings) : null;

        var options = ChunkingOptionsFor(collectionName);
        var profile = ScanProfile.DotNetEnterprise;
        var now = new DateTimeOffset(DateTime.SpecifyKind(_clock.Now, DateTimeKind.Utc));
        var omitted = 0;

        // 1. Trocear cada documento admitido, igual que el productor del origen.
        var prepared = new List<PreparedDocument>(documents.Count);
        foreach (var (path, content) in documents)
        {
            var artifact = DocumentArtifacts.TryCreate(path, content, profile, now);
            if (artifact is null)
            {
                omitted++;
                continue;
            }

            var chunks = new List<CodeChunk>();
            try
            {
                await foreach (var chunk in _chunkRouter.GetStrategy(artifact).ChunkAsync(artifact, content, options, ct))
                {
                    if (IsIndexable(chunk, IndexShortTypeDeclarations))
                        chunks.Add(chunk);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Como el origen: el documento se salta y conserva sus puntos anteriores.
                _logger.LogWarning(ex, "No se pudo trocear '{Path}' en '{Collection}'; se omite.", path, collectionName);
                omitted++;
                continue;
            }

            prepared.Add(new PreparedDocument(
                ChunkBuilder.BuildIdentityKey(options.RepositoryName, artifact.RelativePath),
                Fingerprint(options, path, content, brain),
                chunks));
        }

        // "a/b.cs" y "a\b.cs" son rutas distintas para la validacion pero el mismo documento para la clave de
        // identidad: cada una barreria los puntos de la otra en la siguiente ingesta.
        var duplicate = prepared.GroupBy(d => d.FilePath, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new BadRequestException($"Varias rutas de la solicitud identifican al mismo documento ('{duplicate.Key}').");

        // 2. La coleccion se crea con el perfil y la dimension del modelo, o se rechaza si ya tiene otro (1.7.4); con
        //    resumen opt-in (1.7.7), tambien agrega/verifica el vector dense-resumen. resumenHabilitado es el estado
        //    REAL de la coleccion (puede ser true aunque esta solicitud no haya pedido resumen: es un atributo de la
        //    coleccion, no de la solicitud).
        var resumenHabilitado = await _store.EnsureCollectionAsync(
            collectionName, resuelto.Nombre, brain.ModelName, brain.EmbeddingDimensions,
            resumenSolicitado, resumenEmbeddings?.Brain.ModelName, resumenEmbeddings?.Brain.EmbeddingDimensions ?? 0, ct);

        // 3. Que tiene ya cada documento indexado.
        var existing = await _store.GetDocumentPointsAsync(collectionName, prepared.Select(d => d.FilePath).ToArray(), ct);

        var changed = new List<PreparedDocument>();
        var unchanged = 0;
        foreach (var document in prepared)
        {
            var previous = existing.GetValueOrDefault(document.FilePath) ?? [];
            if (IsUnchanged(document, previous))
                unchanged++;
            else
                changed.Add(document);
        }

        // 4. Vectorizar e indexar por lotes los documentos que cambiaron (denso y sparse sobre el texto enriquecido).
        //    Por longitud, como el banco de 1.6/1.11: un lote de textos parecidos desperdicia menos relleno en el sidecar.
        //    El orden no cambia Ids ni payload.
        var pending = changed.SelectMany(d => d.Chunks.Select(c => (Chunk: c, d.Fingerprint)))
            .OrderBy(p => p.Chunk.EnrichedContent.Length)
            .ToList();
        for (var i = 0; i < pending.Count; i += options.BatchSize)
        {
            var batch = pending.GetRange(i, Math.Min(options.BatchSize, pending.Count - i));
            var texts = batch.Select(b => b.Chunk.EnrichedContent).ToArray();

            var dense = await brain.EmbedDocumentsAsync(texts, ct);
            var sparse = _sparseTokenizer.TokenizeBatch(texts);

            await _store.UpsertBatchAsync(collectionName,
                batch.Select((b, j) => new VectorStoreBatchItem(b.Chunk, dense[j], sparse[j], b.Fingerprint, ResumenPendiente: resumenHabilitado)).ToList(), ct);
        }

        // 5. Barrer los puntos que los documentos procesados ya no generan (DeleteSupersededPointsAsync del origen,
        //    acotado a los documentos de esta solicitud).
        var superseded = changed
            .SelectMany(d =>
            {
                var current = d.Chunks.Select(c => c.Id).ToHashSet();
                return (existing.GetValueOrDefault(d.FilePath) ?? []).Select(p => p.Id).Where(id => !current.Contains(id));
            })
            .Distinct()
            .ToList();
        await _store.DeletePointsAsync(collectionName, superseded, ct);

        _logger.LogInformation(
            "Ingesta en '{Collection}': {Received} documentos, {Indexed} indexados ({Chunks} chunks), {Unchanged} sin cambios, " +
            "{Omitted} omitidos, {Deleted} puntos obsoletos borrados.",
            collectionName, documents.Count, changed.Count, pending.Count, unchanged, omitted, superseded.Count);

        return new IngestionResult(documents.Count, changed.Count, unchanged, omitted, pending.Count, superseded.Count, ResumenHabilitado: resumenHabilitado);
    }

    /// <summary>
    /// Sin cambios: los puntos del documento son exactamente sus Ids actuales y todos llevan su huella. Se comparan
    /// conjuntos: dos chunks con el mismo Id (misma linea y contenido) son un solo punto en Qdrant.
    /// </summary>
    private static bool IsUnchanged(PreparedDocument document, IReadOnlyList<(Guid Id, string? Fingerprint)> previous)
    {
        if (previous.Any(p => p.Fingerprint != document.Fingerprint))
            return false;
        var ids = document.Chunks.Select(c => c.Id).ToHashSet();
        return previous.Count == ids.Count && previous.All(p => ids.Contains(p.Id));
    }

    /// <summary>
    /// Huella de lo que determina los puntos de un documento: contenido (finales de linea normalizados, como el chunker),
    /// ruta literal (va en el encabezado enriquecido), contrato y opciones de chunking, y modelo y dimension del vector.
    /// Si cambia cualquiera, el documento se vuelve a vectorizar aunque sus Ids coincidan.
    /// </summary>
    private static string Fingerprint(ChunkingOptions options, string path, string content, IVectorizationBrain brain)
    {
        var header = string.Join('|',
            $"contrato={ChunkingContract.Version}",
            $"maxTokens={options.MaxTokensPerChunk}",
            $"overlap={options.OverlapTokens}",
            $"oversized={options.OversizedBehavior}",
            $"parentCtx={options.ParentContextLines}",
            $"shortTypes={IndexShortTypeDeclarations}",
            $"repo={options.RepositoryName}",
            $"modelo={brain.ModelName}",
            $"dim={brain.EmbeddingDimensions}",
            $"ruta={path}");
        return ContentHasher.Compute(header + "\n" + SourceLines.Normalize(content));
    }

    private sealed record PreparedDocument(string FilePath, string Fingerprint, List<CodeChunk> Chunks);
}

/// <summary>
/// Conteos de una ingesta: documentos por desenlace, chunks indexados y puntos obsoletos borrados.
/// <see cref="DocumentsSweptAway"/> (1.7.5): documentos barridos por completo por no reaparecer en el proyecto; 0
/// salvo que <see cref="DocumentIngestionPipeline.IngestAsync"/> se haya llamado con <c>barrerDocumentosAusentes: true</c>.
/// <see cref="ResumenHabilitado"/> (1.7.7): si la coleccion (ya existente o recien creada por esta ingesta) tiene de
/// verdad el resumen de negocio habilitado; el llamador (MotorVectorial) solo dispara la fase 2 cuando es true.
/// </summary>
public sealed record IngestionResult(
    int Received, int Indexed, int Unchanged, int Omitted, int ChunksIndexed, int PointsDeleted,
    int DocumentsSweptAway = 0, bool ResumenHabilitado = false);
