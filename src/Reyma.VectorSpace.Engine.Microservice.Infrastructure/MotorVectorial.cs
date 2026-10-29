using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Git;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Ingestion;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Scanning;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure;

/// <summary>
/// Implementa los puertos del contrato (<see cref="IMotorDeEmbeddings"/>, <see cref="IMotorDeBusqueda"/>,
/// <see cref="IMotorDeIngesta"/>, <see cref="IMotorDeIngestaProyecto"/>, <see cref="IMotorDeIngestaProyectoGit"/>) con
/// el motor portado. Recibe la coleccion ya autorizada. Los fallos de Qdrant se traducen a 503 sin exponer al
/// cliente el detalle de la conexion; un perfil de embeddings no registrado o que choca con el de la coleccion
/// (1.7.4) se traduce a 400/409 respectivamente, nunca a 503.
/// </summary>
public sealed class MotorVectorial : IMotorDeEmbeddings, IMotorDeBusqueda, IMotorDeIngesta, IMotorDeIngestaProyecto, IMotorDeIngestaProyectoGit, IMotorDeColecciones
{
    private readonly IVectorizationBrainFactory _brainFactory;
    private readonly QdrantVectorStore _store;
    private readonly QdrantSemanticRetriever _retriever;
    private readonly DocumentIngestionPipeline _pipeline;
    private readonly ResumenPipeline _resumenPipeline;
    private readonly GitOptions _git;
    private readonly ILogger<MotorVectorial> _logger;

    public MotorVectorial(
        IVectorizationBrainFactory brainFactory,
        QdrantVectorStore store,
        QdrantSemanticRetriever retriever,
        DocumentIngestionPipeline pipeline,
        ResumenPipeline resumenPipeline,
        IOptions<MotorOptions> options,
        ILogger<MotorVectorial> logger)
    {
        _brainFactory = brainFactory;
        _store = store;
        _retriever = retriever;
        _pipeline = pipeline;
        _resumenPipeline = resumenPipeline;
        _git = options.Value.Git;
        _logger = logger;
    }

    /// <summary>
    /// 1.7.7: si la ingesta dejo la coleccion con el resumen de negocio habilitado, corre la fase 2 (reanudable) a
    /// continuacion, respetando el tope de tokens de <c>Motor:Resumen:TopeTokensPorCorrida</c> por invocacion. Si no
    /// esta habilitada, no toca el LLM ni gasta tokens.
    /// </summary>
    private async Task<ResumenResultado> ResumirSiCorrespondeAsync(string coleccion, bool resumenHabilitado, CancellationToken ct) =>
        resumenHabilitado ? await _resumenPipeline.ProcesarPendientesAsync(coleccion, ct) : ResumenResultado.Vacio;

    /// <summary>Vectores de documento (sin prefijo de consulta): los mismos que se guardarian al ingestar esos textos.</summary>
    public Task<EmbeddingsResponse> CalcularAsync(string coleccion, IReadOnlyList<string> textos, string? perfil, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var resuelto = _brainFactory.Resolver(perfil);
            await _store.VerificarPerfilParaConsultaAsync(coleccion, resuelto.Nombre, resuelto.Brain.ModelName, resuelto.Brain.EmbeddingDimensions, cancellationToken);
            var vectores = await resuelto.Brain.EmbedDocumentsAsync(textos, cancellationToken);
            return new EmbeddingsResponse(coleccion, resuelto.Brain.ModelName, resuelto.Brain.EmbeddingDimensions, vectores);
        });

    public Task<BusquedaResponse> BuscarAsync(
        string coleccion, string consulta, int topK, string? perfil, IReadOnlyList<string>? simbolosDefinidos,
        EspacioDeBusqueda espacio, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var hits = await _retriever.SearchAsync(coleccion, consulta, topK, perfil, simbolosDefinidos, espacio, cancellationToken);
            return new BusquedaResponse(coleccion,
                hits.Select(h => new ResultadoBusquedaResponse(
                    h.ChunkId, h.Score, h.RelativePath, h.Content,
                    h.LineaInicio, h.LineaFin, h.Lenguaje, h.TipoChunk, h.ContentHash,
                    h.Namespace, h.Clase, h.Metodo,
                    h.SimbolosDefinidos, h.SimbolosConsumidos, h.Resumen)).ToArray());
        });

    public Task<IngestaResponse> IngerirAsync(
        string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, bool resumen, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var resultado = await _pipeline.IngestAsync(coleccion, perfil,
                documentos.Select(d => (d.Ruta!, d.Contenido!)).ToArray(), cancellationToken, resumenSolicitado: resumen);
            var resumenResultado = await ResumirSiCorrespondeAsync(coleccion, resultado.ResumenHabilitado, cancellationToken);
            return new IngestaResponse(Guid.NewGuid(), coleccion, "completada",
                resultado.Received, resultado.Indexed, resultado.Unchanged, resultado.Omitted,
                ResumenesGenerados: resumenResultado.Resumidos, ResumenesPendientes: resumenResultado.Pendientes,
                ResumenTokensConsumidos: resumenResultado.TokensConsumidos);
        });

    /// <summary>
    /// 1.7.5: lee y valida el .zip subido (limites de tamano/entradas/ratio, Zip Slip, enlaces, perfil de escaneo del
    /// motor). Sincrono y rapido (medido en 1.7.3): un .zip invalido o sin documentos admitidos lanza
    /// <see cref="BadRequestException"/> (400) antes de que el comando toque el limite de admision.
    /// </summary>
    public IReadOnlyList<DocumentoRequest> LeerProyectoZip(byte[] zip, string? carpetaRaiz)
    {
        using var stream = new MemoryStream(zip);
        var resultado = LectorZip.Leer(stream, ScanProfile.DotNetEnterprise, new LimitesZip(), carpetaRaiz);
        if (resultado.Error is not null)
            throw new BadRequestException(resultado.Error);
        if (resultado.Documentos.Count == 0)
            throw new BadRequestException("El .zip no contiene documentos admitidos por el perfil de escaneo del servicio.");

        return resultado.Documentos.Select(d => new DocumentoRequest { Ruta = d.Ruta, Contenido = d.Contenido }).ToArray();
    }

    /// <summary>Mismo pipeline que <see cref="IngerirAsync"/>, mas el barrido de documentos ausentes del proyecto (1.7.5).</summary>
    public Task<ResultadoIngestaProyecto> ProcesarAsync(
        string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, bool resumen, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var resultado = await _pipeline.IngestAsync(coleccion, perfil,
                documentos.Select(d => (d.Ruta!, d.Contenido!)).ToArray(), cancellationToken,
                barrerDocumentosAusentes: true, resumenSolicitado: resumen);
            var resumenResultado = await ResumirSiCorrespondeAsync(coleccion, resultado.ResumenHabilitado, cancellationToken);
            return new ResultadoIngestaProyecto(resultado.Received, resultado.Indexed, resultado.Unchanged, resultado.Omitted, resultado.DocumentsSweptAway,
                resumenResultado.Resumidos, resumenResultado.Pendientes, resumenResultado.TokensConsumidos);
        });

    /// <summary>
    /// 1.7.6: valida la URL contra <c>Motor:Git:OrigenesPermitidos</c> sin resolver DNS ni abrir ninguna conexion.
    /// </summary>
    public Uri ValidarUrlGit(string? url)
    {
        var (uri, error) = ListaBlancaGit.Validar(url, _git.OrigenesPermitidos);
        if (error is not null)
            throw new BadRequestException(error);
        return uri!;
    }

    /// <summary>
    /// 1.7.6: clona la URL ya validada en un checkout temporal propio, la escanea con el perfil de escaneo del motor
    /// y ejecuta el mismo pipeline de ingesta de proyecto que el .zip (con barrido de ausentes). El checkout se borra
    /// al terminar, con exito o con error.
    /// </summary>
    public async Task<ResultadoIngestaProyecto> ProcesarGitAsync(
        string coleccion, Uri url, string? carpetaRaiz, string? perfil, bool resumen, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_git.DirectorioTemporal);
        var destino = Path.Combine(_git.DirectorioTemporal, Guid.NewGuid().ToString("N"));

        var cabeceraAutorizacion = LeerCabeceraAutorizacionDesdeEntorno();
        var opciones = new OpcionesClonado
        {
            Git = _git.Git,
            Profundidad = _git.Profundidad,
            MaxBytes = _git.MaxBytesCheckout,
            Plazo = TimeSpan.FromSeconds(_git.PlazoSegundos),
            CabeceraAutorizacion = cabeceraAutorizacion
        };

        try
        {
            var clonado = await ClonadorGit.ClonarAsync(url, destino, opciones, permitirHttp: url.Scheme == "http", cancellationToken);
            if (!clonado.Exito)
            {
                _logger.LogError("Fallo el clonado de '{Coleccion}' ({Bytes} bytes, {Segundos} s): {Motivo}. {Stderr}",
                    coleccion, clonado.Bytes, clonado.Segundos, clonado.Motivo, clonado.Stderr);
                throw new BadRequestException(clonado.Motivo ?? "El clonado del repositorio fallo.");
            }

            var raizEfectiva = ResolverCarpetaRaiz(destino, carpetaRaiz);
            var escaneo = EscanerDeCheckout.Escanear(raizEfectiva, ScanProfile.DotNetEnterprise);
            if (escaneo.Documentos.Count == 0)
                throw new BadRequestException("El repositorio no contiene documentos admitidos por el perfil de escaneo del servicio.");

            var documentos = escaneo.Documentos.Select(d => (d.Ruta, d.Contenido)).ToArray();
            return await ConQdrantAsync(async () =>
            {
                var resultado = await _pipeline.IngestAsync(coleccion, perfil, documentos, cancellationToken,
                    barrerDocumentosAusentes: true, resumenSolicitado: resumen);
                var resumenResultado = await ResumirSiCorrespondeAsync(coleccion, resultado.ResumenHabilitado, cancellationToken);
                return new ResultadoIngestaProyecto(resultado.Received, resultado.Indexed, resultado.Unchanged, resultado.Omitted, resultado.DocumentsSweptAway,
                    resumenResultado.Resumidos, resumenResultado.Pendientes, resumenResultado.TokensConsumidos);
            });
        }
        finally
        {
            ClonadorGit.Borrar(destino);
        }
    }

    /// <summary>
    /// Igual que <c>LectorZip.NormalizarRuta</c> para el prefijo: valida que <paramref name="carpetaRaiz"/> no escape
    /// del checkout (sin "..", sin ruta absoluta, sin ':' de unidad) antes de resolverla a una ruta real en disco.
    /// </summary>
    private static string ResolverCarpetaRaiz(string destino, string? carpetaRaiz)
    {
        if (string.IsNullOrWhiteSpace(carpetaRaiz))
            return destino;

        var normalizada = carpetaRaiz.Trim('/', '\\').Replace('\\', '/');
        if (normalizada.Contains(':') || normalizada.Split('/').Any(s => s is "" or "." or ".."))
            throw new BadRequestException("CarpetaRaiz no puede ser una ruta absoluta ni salir de la raiz del repositorio.");

        var raiz = Path.GetFullPath(Path.Combine(destino, normalizada));
        if (!raiz.StartsWith(Path.GetFullPath(destino) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new BadRequestException("CarpetaRaiz no puede salir de la raiz del repositorio.");
        if (!Directory.Exists(raiz))
            throw new BadRequestException($"La carpeta raiz '{carpetaRaiz}' no existe en el repositorio.");
        return raiz;
    }

    /// <summary>
    /// Identidad del servicio frente a Azure DevOps (1.7.6): un PAT en <c>Motor:Git:NombreVariableIdentidad</c>
    /// (variable de entorno, nunca appsettings) se convierte en la cabecera <c>Authorization: Basic ...</c> que
    /// Azure DevOps espera para autenticacion basica con PAT (usuario vacio). Sin esa variable, null: el clonado no
    /// aisla la configuracion del host y deja que su credential helper ya configurado resuelva la autenticacion
    /// (valido solo para la verificacion puntual de 1.7.6 descrita en el ledger). El valor nunca se registra en logs.
    /// </summary>
    private string? LeerCabeceraAutorizacionDesdeEntorno()
    {
        var pat = Environment.GetEnvironmentVariable(_git.NombreVariableIdentidad);
        if (string.IsNullOrWhiteSpace(pat))
            return null;
        return "Basic " + Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(":" + pat));
    }

    /// <summary>1.16: nombre y perfil de embeddings de cada coleccion existente, directo de Qdrant.</summary>
    public Task<IReadOnlyList<ColeccionInfoResponse>> ListarAsync(CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var colecciones = await _store.ListarColeccionesAsync(cancellationToken);
            return (IReadOnlyList<ColeccionInfoResponse>)colecciones
                .Select(c => new ColeccionInfoResponse(c.Nombre, c.Perfil?.Perfil, c.Perfil?.Modelo, c.Perfil?.Dimension))
                .ToList();
        });

    /// <summary>
    /// 1.19: ruta (file_path sin el prefijo "{coleccion}/"), huella y cantidad de puntos de cada documento indexado,
    /// directo de Qdrant. La coleccion es siempre el <c>repositoryName</c> con el que se construyo el file_path
    /// (ChunkBuilder.BuildIdentityKey / DocumentIngestionPipeline), asi que el prefijo a quitar es exacto. Huella
    /// null si los puntos del documento no comparten un solo valor (mismo criterio que IsUnchanged del pipeline de
    /// ingesta: cualquier discrepancia, incluida la de un punto sin huella guardada, se trata como "no coincide").
    /// </summary>
    public Task<IReadOnlyList<DocumentoIndexadoResponse>> ListarDocumentosAsync(string coleccion, CancellationToken cancellationToken) =>
        ConQdrantAsync(async () =>
        {
            var documentos = await _store.ListarDocumentosConHuellaAsync(coleccion, cancellationToken);
            var prefijo = coleccion + "/";
            return (IReadOnlyList<DocumentoIndexadoResponse>)documentos
                .Select(kv =>
                {
                    var ruta = kv.Key.StartsWith(prefijo, StringComparison.Ordinal) ? kv.Key[prefijo.Length..] : kv.Key;
                    var huellas = kv.Value.Select(p => p.Fingerprint).Distinct(StringComparer.Ordinal).ToList();
                    var huella = huellas.Count == 1 ? huellas[0] : null;
                    return new DocumentoIndexadoResponse(ruta, huella, kv.Value.Count);
                })
                .OrderBy(d => d.Ruta, StringComparer.Ordinal)
                .ToList();
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
