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

    /// <summary><paramref name="mensaje"/> es para que quien llama lo registre en el log; <see cref="IngestaResponse"/>
    /// no tiene un campo de error todavia, asi que aqui solo se guarda el estado.</summary>
    void MarcarError(Guid ingestaId, string mensaje);

    /// <summary>Null si no existe o si existe pero es de otra coleccion (no se expone esa diferencia al cliente).</summary>
    IngestaResponse? Obtener(string coleccion, Guid ingestaId);
}

public sealed class RegistroDeIngestasProyecto : IRegistroDeIngestasProyecto
{
    private sealed record Entrada(
        string Coleccion, int Recibidos, string Estado, int Indexados, int SinCambios, int Omitidos, int Eliminados);

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
                Eliminados = resultado.DocumentosEliminados
            });

    public void MarcarError(Guid ingestaId, string mensaje) =>
        _entradas.AddOrUpdate(ingestaId,
            _ => throw IngestaDesconocida(ingestaId),
            (_, e) => e with { Estado = EstadosIngestaProyecto.Error });

    public IngestaResponse? Obtener(string coleccion, Guid ingestaId)
    {
        if (!_entradas.TryGetValue(ingestaId, out var e) || !string.Equals(e.Coleccion, coleccion, StringComparison.Ordinal))
            return null;
        return new IngestaResponse(ingestaId, coleccion, e.Estado, e.Recibidos, e.Indexados, e.SinCambios, e.Omitidos, e.Eliminados);
    }

    private static InvalidOperationException IngestaDesconocida(Guid ingestaId) =>
        new($"La ingesta de proyecto '{ingestaId}' no fue creada con Crear antes de actualizarla.");
}
