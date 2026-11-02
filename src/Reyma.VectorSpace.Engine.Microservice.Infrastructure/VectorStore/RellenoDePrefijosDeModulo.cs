using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;

/// <summary>
/// Relleno explicito de <see cref="ModulePrefixes"/> en una coleccion anterior al item 1.21.1. Solo escribe dos
/// claves de payload (<c>module_prefixes</c> y <c>module_prefixes_version</c>) con SetPayload, los indices que falten
/// y la marca de la coleccion: no vectoriza, no llama a ningun LLM y no toca Ids, vectores, hashes, huellas ni
/// resumenes (por eso no depende de ningun brain).
/// <para>
/// Idempotente y reanudable: cada pagina toma solo puntos sin la version actual, asi que relanzarlo continua donde
/// quedo y una coleccion ya completa no recibe escrituras. Cada SetPayload lleva un filtro de comparacion
/// (Id + sin version + el mismo namespace/ruta leidos) para no aplicar prefijos derivados de valores que otro escritor
/// cambio entre la lectura y la escritura.
/// </para>
/// <para>
/// Exclusion: marca <c>en_curso:&lt;run&gt;:&lt;expira&gt;</c> con lease renovado por pagina. Las ingestas de este
/// servicio la ven y responden 409; otro relleno con lease vivo es rechazado. Qdrant no ofrece compare-and-set de
/// metadata: dos rellenos simultaneos que se pisen no corrompen datos (escrituras deterministas e idempotentes) y el
/// perdedor aborta al verificar su lease. Un escritor antiguo (sin este codigo) no ve la marca: el procedimiento exige
/// detenerlo o actualizarlo; si escribe igualmente, el conteo final impide marcar completo y, si escribe despues, la
/// busqueda con modulos responde 409 hasta relanzar el relleno. Un error o cancelacion deja la marca en curso con el
/// lease expirado: nunca completo.
/// </para>
/// </summary>
public sealed class RellenoDePrefijosDeModulo
{
    public const int LotePorDefecto = 256;
    public static readonly TimeSpan LeasePorDefecto = TimeSpan.FromMinutes(5);

    private readonly QdrantClient _client;
    private readonly HttpClient _rest;
    private readonly ILogger _logger;

    public RellenoDePrefijosDeModulo(QdrantClient client, HttpClient rest, ILogger logger)
    {
        _client = client;
        _rest = rest;
        _logger = logger;
    }

    /// <summary>Lease de cada renovacion; configurable para pruebas de expiracion.</summary>
    public TimeSpan Lease { get; init; } = LeasePorDefecto;

    /// <summary>Gancho de pruebas tras escribir cada pagina (numero de pagina, base 1): inyectar fallos o cancelacion.</summary>
    public Func<int, CancellationToken, Task>? TrasPagina { get; init; }

    public async Task<ResultadoRellenoModulo> EjecutarAsync(string coleccion, int lote, CancellationToken ct)
    {
        if (lote is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(lote), "El lote debe estar entre 1 y 10000.");
        if (!await _client.CollectionExistsAsync(coleccion, ct))
            throw new RellenoModuloException($"La coleccion '{coleccion}' no existe.", RellenoModuloError.ColeccionInexistente);

        var marcaInicial = await ModulePrefixes.LeerMarcaAsync(_rest, coleccion, ct);
        var indicesIniciales = await ModulePrefixes.LeerIndicesAsync(_client, coleccion, ct);
        if (indicesIniciales.Conflicto is not null)
            throw new RellenoModuloException($"Indice incompatible: {indicesIniciales.Conflicto}.", RellenoModuloError.IndiceIncompatible);

        if (marcaInicial == ModulePrefixes.MarcaCompleta && indicesIniciales.Prefijos && indicesIniciales.Version &&
            await ModulePrefixes.ContarSinPrepararAsync(_client, coleccion, ct) == 0)
        {
            _logger.LogInformation("Coleccion '{Coleccion}' ya preparada para el filtro de modulo; sin escrituras.", coleccion);
            return new ResultadoRellenoModulo(YaCompleta: true, PuntosActualizados: 0, Paginas: 0);
        }

        if (ModulePrefixes.TryParseEnCurso(marcaInicial, out var otroRun, out var expira) && expira > DateTimeOffset.UtcNow)
            throw new RellenoModuloException(
                $"Otro relleno ('{otroRun}') tiene el lease vivo hasta {expira:O}.", RellenoModuloError.LeaseOcupado);

        var run = Guid.NewGuid().ToString("N");
        await TomarLeaseAsync(coleccion, run, ct);

        var puntos = 0L;
        var paginas = 0;
        try
        {
            if (!indicesIniciales.Prefijos)
                await _client.CreatePayloadIndexAsync(coleccion, ModulePrefixes.PayloadKey, PayloadSchemaType.Keyword, cancellationToken: ct);
            if (!indicesIniciales.Version)
                await _client.CreatePayloadIndexAsync(coleccion, ModulePrefixes.VersionPayloadKey, PayloadSchemaType.Integer, cancellationToken: ct);

            HashSet<string>? firmaAnterior = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var pagina = await _client.ScrollAsync(coleccion, ModulePrefixes.SinPreparar(), limit: (uint)lote,
                    payloadSelector: new WithPayloadSelector
                    {
                        Include = new PayloadIncludeSelector { Fields = { "namespace", "relative_path" } }
                    },
                    vectorsSelector: new WithVectorsSelector { Enable = false },
                    cancellationToken: ct);
                if (pagina.Result.Count == 0)
                    break;

                // Ids UUID (los que escribe el servicio) o numericos (colecciones escritas por otros procesos).
                var grupos = new Dictionary<(string? Ns, string? Ruta), List<PointId>>();
                var firma = new HashSet<string>(StringComparer.Ordinal);
                foreach (var punto in pagina.Result)
                {
                    var ns = LeerCadena(punto, "namespace");
                    var ruta = LeerCadena(punto, "relative_path");
                    var id = punto.Id;
                    var clave = id.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.Num ? $"n{id.Num}" : $"u{id.Uuid}";
                    firma.Add($"{clave}\u0001{ns}\u0001{ruta}");
                    if (!grupos.TryGetValue((ns, ruta), out var ids))
                        grupos[(ns, ruta)] = ids = [];
                    ids.Add(id);
                }

                // Misma pagina con los mismos valores tras escribirla: el SetPayload no tuvo efecto y no hay progreso.
                if (firmaAnterior is not null && firma.SetEquals(firmaAnterior))
                    throw new RellenoModuloException(
                        "El relleno no avanza (la misma pagina sigue sin preparar tras escribirla).", RellenoModuloError.SinProgreso);
                firmaAnterior = firma;

                foreach (var ((ns, ruta), ids) in grupos)
                {
                    var valores = new Dictionary<string, Value>
                    {
                        [ModulePrefixes.PayloadKey] = ModulePrefixes.ToPayloadValue(ModulePrefixes.Derive(ns, ruta)),
                        [ModulePrefixes.VersionPayloadKey] = new Value { IntegerValue = ModulePrefixes.Version },
                    };
                    var cas = ModulePrefixes.SinPreparar();
                    cas.Must.Add(new Condition { HasId = new HasIdCondition { HasId = { ids } } });
                    cas.Must.Add(IgualA("namespace", ns));
                    cas.Must.Add(IgualA("relative_path", ruta));
                    await _client.SetPayloadAsync(coleccion, valores, cas, wait: true, cancellationToken: ct);
                }

                puntos += pagina.Result.Count;
                paginas++;
                _logger.LogInformation("Relleno de modulo '{Coleccion}': pagina {Pagina}, {Puntos} puntos tratados.", coleccion, paginas, puntos);

                await VerificarYRenovarLeaseAsync(coleccion, run, ct);
                if (TrasPagina is not null)
                    await TrasPagina(paginas, ct);
            }

            var indices = await ModulePrefixes.LeerIndicesAsync(_client, coleccion, ct);
            if (!indices.Prefijos || !indices.Version)
                throw new RellenoModuloException("Faltan los indices del filtro de modulo al terminar.", RellenoModuloError.IndiceIncompatible);
            await VerificarYRenovarLeaseAsync(coleccion, run, ct);
            if (await ModulePrefixes.ContarSinPrepararAsync(_client, coleccion, ct) != 0)
                throw new RellenoModuloException("Quedan puntos sin preparar al terminar.", RellenoModuloError.Incompleto);

            await ModulePrefixes.EscribirMarcaAsync(_rest, coleccion, ModulePrefixes.MarcaCompleta, ct);

            // Un escritor antiguo pudo agregar puntos entre el conteo y la marca: se vuelve a contar ya marcada.
            var tardios = await ModulePrefixes.ContarSinPrepararAsync(_client, coleccion, ct);
            if (tardios != 0)
            {
                await ModulePrefixes.EscribirMarcaAsync(_rest, coleccion, ModulePrefixes.MarcaEnCurso(run, DateTimeOffset.UnixEpoch), CancellationToken.None);
                throw new RellenoModuloException(
                    $"{tardios} puntos sin preparar aparecieron al marcar completo (escritor antiguo activo); relanzar tras detenerlo.",
                    RellenoModuloError.Incompleto);
            }

            _logger.LogInformation("Relleno de modulo '{Coleccion}' completo: {Puntos} puntos en {Paginas} paginas.", coleccion, puntos, paginas);
            return new ResultadoRellenoModulo(YaCompleta: false, PuntosActualizados: puntos, Paginas: paginas);
        }
        catch (Exception ex) when (ex is not RellenoModuloException { Codigo: RellenoModuloError.LeasePerdido })
        {
            await LiberarLeaseComoInterrumpidoAsync(coleccion, run);
            throw;
        }
    }

    /// <summary>Solo lectura: marca, indices y puntos sin preparar (conteo exacto). Completa = lo que exige la busqueda.</summary>
    public async Task<EstadoFiltroModulo> VerificarAsync(string coleccion, CancellationToken ct)
    {
        if (!await _client.CollectionExistsAsync(coleccion, ct))
            throw new RellenoModuloException($"La coleccion '{coleccion}' no existe.", RellenoModuloError.ColeccionInexistente);
        var marca = await ModulePrefixes.LeerMarcaAsync(_rest, coleccion, ct);
        var indices = await ModulePrefixes.LeerIndicesAsync(_client, coleccion, ct);
        var sinPreparar = await ModulePrefixes.ContarSinPrepararAsync(_client, coleccion, ct);
        var completa = marca == ModulePrefixes.MarcaCompleta && indices.Prefijos && indices.Version && indices.Conflicto is null && sinPreparar == 0;
        return new EstadoFiltroModulo(marca, indices.Prefijos, indices.Version, indices.Conflicto, sinPreparar, completa);
    }

    private async Task TomarLeaseAsync(string coleccion, string run, CancellationToken ct)
    {
        await ModulePrefixes.EscribirMarcaAsync(_rest, coleccion, ModulePrefixes.MarcaEnCurso(run, DateTimeOffset.UtcNow + Lease), ct);
        if (!ModulePrefixes.TryParseEnCurso(await ModulePrefixes.LeerMarcaAsync(_rest, coleccion, ct), out var leido, out _) || leido != run)
            throw new RellenoModuloException("Otro relleno tomo el lease al mismo tiempo.", RellenoModuloError.LeaseOcupado);
    }

    private async Task VerificarYRenovarLeaseAsync(string coleccion, string run, CancellationToken ct)
    {
        if (!ModulePrefixes.TryParseEnCurso(await ModulePrefixes.LeerMarcaAsync(_rest, coleccion, ct), out var leido, out _) || leido != run)
            throw new RellenoModuloException("El lease del relleno lo tomo otro proceso; este relleno se detiene.", RellenoModuloError.LeasePerdido);
        await ModulePrefixes.EscribirMarcaAsync(_rest, coleccion, ModulePrefixes.MarcaEnCurso(run, DateTimeOffset.UtcNow + Lease), ct);
    }

    /// <summary>Best effort: deja la marca en curso pero expirada, para que un relanzamiento la tome sin esperar.</summary>
    private async Task LiberarLeaseComoInterrumpidoAsync(string coleccion, string run)
    {
        try
        {
            if (ModulePrefixes.TryParseEnCurso(await ModulePrefixes.LeerMarcaAsync(_rest, coleccion, CancellationToken.None), out var leido, out _) &&
                leido == run)
                await ModulePrefixes.EscribirMarcaAsync(_rest, coleccion, ModulePrefixes.MarcaEnCurso(run, DateTimeOffset.UnixEpoch), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo marcar el lease del relleno de '{Coleccion}' como interrumpido; expirara solo.", coleccion);
        }
    }

    private static string? LeerCadena(RetrievedPoint punto, string clave)
    {
        if (!punto.Payload.TryGetValue(clave, out var valor) || valor.KindCase == Value.KindOneofCase.NullValue)
            return null;
        if (valor.KindCase != Value.KindOneofCase.StringValue)
            throw new RellenoModuloException(
                $"El punto {punto.Id.Uuid} tiene '{clave}' de tipo {valor.KindCase}; se esperaba texto.", RellenoModuloError.PayloadInesperado);
        return valor.StringValue;
    }

    private static Condition IgualA(string clave, string? valor) =>
        valor is null ? Conditions.IsEmpty(clave) : Conditions.MatchKeyword(clave, valor);
}

public sealed record ResultadoRellenoModulo(bool YaCompleta, long PuntosActualizados, int Paginas);

public sealed record EstadoFiltroModulo(
    string? Marca, bool IndicePrefijos, bool IndiceVersion, string? ConflictoIndice, ulong PuntosSinPreparar, bool Completa);

public enum RellenoModuloError
{
    ColeccionInexistente = 1,
    LeaseOcupado = 2,
    LeasePerdido = 3,
    IndiceIncompatible = 4,
    Incompleto = 5,
    SinProgreso = 6,
    PayloadInesperado = 7,
}

public sealed class RellenoModuloException(string message, RellenoModuloError codigo) : Exception(message)
{
    public RellenoModuloError Codigo { get; } = codigo;
}
