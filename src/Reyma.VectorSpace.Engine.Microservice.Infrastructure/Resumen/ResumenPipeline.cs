using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;

/// <summary>Conteos de una corrida de la fase 2 (1.7.7).</summary>
public sealed record ResumenResultado(int Resumidos, int Pendientes, int TokensConsumidos, int SinContenidoDeNegocio)
{
    public static readonly ResumenResultado Vacio = new(0, 0, 0, 0);
}

/// <summary>
/// Fase 2 reanudable del resumen de negocio (1.7.7, revive OllamaBusinessSummaryGenerator/DefaultIngestionPipeline de
/// Rag.Context.Engine sobre el transporte HTTP de este servicio): procesa los puntos marcados
/// <c>resumen_pending=true</c> de una coleccion con el resumen habilitado, hasta agotar
/// <see cref="ResumenOptions.TopeTokensPorCorrida"/> o los pendientes. Lo que no alcanza a procesar en esta corrida
/// queda <c>resumen_pending=true</c> para la siguiente (ninguno de los ya resumidos se repite).
///
/// Aislamiento de fallos, igual que el origen: una respuesta vacia del LLM para UN chunk se salta (aislado,
/// recuperable en la siguiente corrida); un perfil de LLM mal configurado (modelo no permitido/de otra capacidad)
/// se propaga como <see cref="BadRequestException"/>; cualquier otra falla (conexion, Qdrant) corta esta corrida
/// entera dejando el resto pendiente, en vez de insistir chunk a chunk contra una dependencia caida.
/// </summary>
public sealed class ResumenPipeline
{
    private readonly QdrantVectorStore _store;
    private readonly IVectorizationBrainFactory _embeddingsFactory;
    private readonly ILlmResumenBrain _llm;
    private readonly ICacheDeResumenes _cache;
    private readonly ResumenOptions _options;
    private readonly ILogger<ResumenPipeline> _logger;

    public ResumenPipeline(
        QdrantVectorStore store,
        IVectorizationBrainFactory embeddingsFactory,
        ILlmResumenBrain llm,
        ICacheDeResumenes cache,
        IOptions<MotorOptions> options,
        ILogger<ResumenPipeline> logger)
    {
        _store = store;
        _embeddingsFactory = embeddingsFactory;
        _llm = llm;
        _cache = cache;
        _options = options.Value.Resumen;
        _logger = logger;
    }

    public async Task<ResumenResultado> ProcesarPendientesAsync(string coleccion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.PerfilEmbeddings))
            return ResumenResultado.Vacio; // el llamador ya deberia haber evitado esto (resumen no habilitado aqui)

        var resueltoEmbeddings = _embeddingsFactory.Resolver(_options.PerfilEmbeddings);
        var promptVersion = _llm.PromptVersion;
        var modelo = _llm.ModelName;

        var tokensGastados = 0;
        var resumidos = 0;
        var sinNegocio = 0;
        var cortado = false;

        PointId? offset = null;
        do
        {
            var pagina = await _store.ListarPendientesDeResumenAsync(coleccion, _options.TamanoPaginaPendientes, offset, ct);
            foreach (var punto in pagina.Puntos)
            {
                if (tokensGastados >= _options.TopeTokensPorCorrida)
                {
                    cortado = true;
                    break;
                }

                string texto;
                bool esContenidoDeNegocio;
                if (_cache.TryObtener(punto.ContentHash, promptVersion, modelo, out var cacheado))
                {
                    texto = cacheado.Texto;
                    esContenidoDeNegocio = !cacheado.SinContenidoDeNegocio;
                }
                else
                {
                    ResumenLlmResultado? resultado;
                    try
                    {
                        resultado = await _llm.ResumirAsync(punto.Lenguaje, punto.RutaRelativa, punto.Contenido, ct);
                    }
                    catch (BadRequestException)
                    {
                        throw; // configuracion invalida: no se arregla reintentando otros chunks, se propaga (400)
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "Fallo el resumen de negocio de '{Punto}' en '{Coleccion}'; se corta esta corrida, queda pendiente.",
                            punto.Id, coleccion);
                        cortado = true;
                        break;
                    }

                    if (resultado is null)
                        continue; // fallo de contenido aislado a este chunk (respuesta vacia): sigue pendiente

                    texto = resultado.Texto;
                    esContenidoDeNegocio = !resultado.SinContenidoDeNegocio;
                    tokensGastados += resultado.TokensConsumidos;
                    _cache.Guardar(punto.ContentHash, promptVersion, modelo, new ResumenCacheEntrada(!esContenidoDeNegocio, texto));
                }

                float[]? vector = null;
                if (esContenidoDeNegocio)
                    vector = (await resueltoEmbeddings.Brain.EmbedDocumentsAsync([texto], ct))[0];
                else
                    sinNegocio++;

                await _store.ActualizarResumenAsync(coleccion, punto.Id, vector, modelo, promptVersion, ct);
                resumidos++;
            }

            offset = cortado ? null : pagina.SiguienteOffset;
        } while (offset is not null);

        var pendientes = await _store.ContarPendientesDeResumenAsync(coleccion, ct);
        return new ResumenResultado(resumidos, (int)Math.Min(pendientes, int.MaxValue), tokensGastados, sinNegocio);
    }
}
