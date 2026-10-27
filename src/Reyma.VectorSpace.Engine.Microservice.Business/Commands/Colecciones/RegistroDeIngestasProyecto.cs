using System.Collections.Concurrent;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;

/// <summary>Valores de <see cref="IngestaResponse.Estado"/> para el origen 'proyecto' (1.7.5), asincrono.</summary>
public static class EstadosIngestaProyecto
{
    public const string Pendiente = "pendiente";
    public const string Procesando = "procesando";
    public const string Completada = "completada";
    public const string Error = "error";
}

/// <summary>
/// Estado de las ingestas de proyecto en curso o terminadas (1.7.5): en memoria, por instancia, igual filosofia que
/// <see cref="ILimiteDeIngestas"/> (varias replicas no comparten este registro ni el cupo). No persiste entre
/// reinicios: una ingesta en curso cuando el proceso se reinicia pierde su estado (el trabajo en Qdrant ya hecho no
/// se deshace, pero su <c>ingestaId</c> deja de poder consultarse).
/// </summary>
public interface IRegistroDeIngestasProyecto
{
    /// <summary>Registra una ingesta nueva en estado "pendiente" y devuelve su identificador.</summary>
    Guid Crear(string coleccion, int documentosRecibidos);

    void MarcarProcesando(Guid ingestaId);

    void MarcarCompletada(Guid ingestaId, ResultadoIngestaProyecto resultado);

    /// <summary><paramref name="mensaje"/> queda tanto en el log de quien llama como en <see cref="IngestaResponse.MensajeError"/>.</summary>
    void MarcarError(Guid ingestaId, string mensaje);

    /// <summary>Null si no existe o si existe pero es de otra coleccion (no se expone esa diferencia al cliente).</summary>
    IngestaResponse? Obtener(string coleccion, Guid ingestaId);

    /// <summary>
    /// Entradas en cualquier estado; vacio si no hay ninguna coincidencia (item 1.15, nunca el equivalente a un 404:
    /// no tener ingestas no es un error). <paramref name="coleccion"/> null o vacio trae las de cualquier coleccion
    /// (item 1.16, para cuando tampoco se sabe que coleccion buscar); con un valor, solo las de esa coleccion. Sin
    /// orden garantizado. Mismo alcance en memoria que el resto del registro: solo ve lo que paso desde el ultimo
    /// arranque de esta instancia.
    /// </summary>
    IReadOnlyList<IngestaResponse> Listar(string? coleccion = null);
}

public sealed class RegistroDeIngestasProyecto : IRegistroDeIngestasProyecto
{
    private sealed record Entrada(
        string Coleccion, int Recibidos, string Estado, int Indexados, int SinCambios, int Omitidos, int Eliminados,
        int ResumenesGenerados = 0, int ResumenesPendientes = 0, int ResumenTokensConsumidos = 0, string? Mensaje = null);

    private readonly ConcurrentDictionary<Guid, Entrada> _entradas = new();

    public Guid Crear(string coleccion, int documentosRecibidos)
    {
        var id = Guid.NewGuid();
        _entradas[id] = new Entrada(coleccion, documentosRecibidos, EstadosIngestaProyecto.Pendiente, 0, 0, 0, 0);
        return id;
    }

    public void MarcarProcesando(Guid ingestaId) =>
        _entradas.AddOrUpdate(ingestaId,
            _ => throw IngestaDesconocida(ingestaId),
            (_, e) => e with { Estado = EstadosIngestaProyecto.Procesando });

    public void MarcarCompletada(Guid ingestaId, ResultadoIngestaProyecto resultado) =>
        _entradas.AddOrUpdate(ingestaId,
            _ => throw IngestaDesconocida(ingestaId),
            (_, e) => e with
            {
                Estado = EstadosIngestaProyecto.Completada,
                Indexados = resultado.Indexados,
                SinCambios = resultado.SinCambios,
                Omitidos = resultado.Omitidos,
                Eliminados = resultado.DocumentosEliminados,
                ResumenesGenerados = resultado.ResumenesGenerados,
                ResumenesPendientes = resultado.ResumenesPendientes,
                ResumenTokensConsumidos = resultado.ResumenTokensConsumidos
            });

    public void MarcarError(Guid ingestaId, string mensaje) =>
        _entradas.AddOrUpdate(ingestaId,
            _ => throw IngestaDesconocida(ingestaId),
            (_, e) => e with { Estado = EstadosIngestaProyecto.Error, Mensaje = mensaje });

    public IngestaResponse? Obtener(string coleccion, Guid ingestaId)
    {
        if (!_entradas.TryGetValue(ingestaId, out var e) || !string.Equals(e.Coleccion, coleccion, StringComparison.Ordinal))
            return null;
        return AResponse(ingestaId, e);
    }

    public IReadOnlyList<IngestaResponse> Listar(string? coleccion = null) =>
        _entradas
            .Where(par => string.IsNullOrEmpty(coleccion) || string.Equals(par.Value.Coleccion, coleccion, StringComparison.Ordinal))
            .Select(par => AResponse(par.Key, par.Value))
            .ToList();

    private static IngestaResponse AResponse(Guid ingestaId, Entrada e) =>
        new(ingestaId, e.Coleccion, e.Estado, e.Recibidos, e.Indexados, e.SinCambios, e.Omitidos, e.Eliminados,
            e.ResumenesGenerados, e.ResumenesPendientes, e.ResumenTokensConsumidos, e.Mensaje);

    private static InvalidOperationException IngestaDesconocida(Guid ingestaId) =>
        new($"La ingesta de proyecto '{ingestaId}' no fue creada con Crear antes de actualizarla.");
}
