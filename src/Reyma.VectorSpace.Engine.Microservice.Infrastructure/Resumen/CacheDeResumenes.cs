using System.Collections.Concurrent;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;

/// <summary>Resumen ya generado para un (content_hash, version de prompt, modelo) dado (1.7.7).</summary>
public sealed record ResumenCacheEntrada(bool SinContenidoDeNegocio, string Texto);

/// <summary>
/// Cache de resumenes de negocio por <c>content_hash</c> + version de prompt + modelo (1.7.7, criterio 3): si el
/// mismo contenido ya se resumio con el mismo prompt y modelo, se reutiliza sin gastar tokens de LLM. En memoria, por
/// instancia, igual filosofia que <see cref="Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones.LimiteDeIngestas"/>:
/// no persiste entre reinicios ni se comparte entre replicas.
///
/// La garantia principal de "reingerir sin cambios gasta 0 tokens" ya la da
/// <see cref="Reyma.VectorSpace.Engine.Microservice.Infrastructure.Ingestion.DocumentIngestionPipeline"/> al no tocar
/// un documento sin cambios (ni siquiera entra a la cola de pendientes de resumen); esta cache cubre, ademas, el
/// contenido identico que aparece en mas de un punto (p. ej. el mismo fragmento duplicado en dos archivos) dentro de
/// la vida de esta instancia.
/// </summary>
public interface ICacheDeResumenes
{
    bool TryObtener(string contentHash, string promptVersion, string modelo, out ResumenCacheEntrada entrada);

    void Guardar(string contentHash, string promptVersion, string modelo, ResumenCacheEntrada entrada);
}

public sealed class CacheDeResumenes : ICacheDeResumenes
{
    private readonly ConcurrentDictionary<string, ResumenCacheEntrada> _cache = new(StringComparer.Ordinal);

    public bool TryObtener(string contentHash, string promptVersion, string modelo, out ResumenCacheEntrada entrada) =>
        _cache.TryGetValue(Clave(contentHash, promptVersion, modelo), out entrada!);

    public void Guardar(string contentHash, string promptVersion, string modelo, ResumenCacheEntrada entrada) =>
        _cache[Clave(contentHash, promptVersion, modelo)] = entrada;

    private static string Clave(string contentHash, string promptVersion, string modelo) => $"{contentHash}|{promptVersion}|{modelo}";
}
