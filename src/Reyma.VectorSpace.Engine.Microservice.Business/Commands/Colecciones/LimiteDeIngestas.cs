using System.Net;
using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;

/// <summary>
/// Seccion <c>LimiteDeIngestas</c> (item 1.7): cuantas ingestas corren a la vez en esta instancia y cuantas pueden
/// esperar turno. Todas comparten el sidecar de embeddings; con TEI en CPU dos ingestas simultaneas no terminan antes
/// que una tras otra (ver docs/evaluaciones/1.7/decision.md), solo alargan cada solicitud y las busquedas.
/// </summary>
public sealed class LimiteDeIngestasOptions
{
    public const string Seccion = "LimiteDeIngestas";

    /// <summary>Ingestas en curso a la vez en esta instancia.</summary>
    public int MaximoSimultaneas { get; set; } = 1;

    /// <summary>Ingestas que esperan turno; la siguiente responde 429 sin entrar.</summary>
    public int MaximoEnCola { get; set; } = 4;

    /// <summary>Valor de la cabecera Retry-After del 429.</summary>
    public int SegundosParaReintentar { get; set; } = 60;
}

/// <summary>Admision de ingestas: un permiso por solicitud, que se libera al terminar (o al cancelarse).</summary>
public interface ILimiteDeIngestas
{
    /// <summary>
    /// Espera turno si hay hueco en la cola; si no, lanza <see cref="IngestasSaturadasException"/> sin esperar. Cancelar
    /// <paramref name="cancellationToken"/> mientras espera libera el lugar en la cola.
    /// </summary>
    Task<IDisposable> EntrarAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Limite por instancia (en proceso). Varias replicas multiplican el limite y no comparten cola: para eso haria falta
/// un limitador externo, que hoy no existe porque el servicio corre en una sola instancia.
/// </summary>
public sealed class LimiteDeIngestas : ILimiteDeIngestas
{
    private readonly SemaphoreSlim _enCurso;
    private readonly int _maximoAdmitidas;
    private readonly int _segundosParaReintentar;
    private int _admitidas;

    public LimiteDeIngestas(IOptions<LimiteDeIngestasOptions> options)
    {
        var o = options.Value;
        _enCurso = new SemaphoreSlim(o.MaximoSimultaneas, o.MaximoSimultaneas);
        _maximoAdmitidas = o.MaximoSimultaneas + o.MaximoEnCola;
        _segundosParaReintentar = o.SegundosParaReintentar;
    }

    public async Task<IDisposable> EntrarAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _admitidas) > _maximoAdmitidas)
        {
            Interlocked.Decrement(ref _admitidas);
            throw new IngestasSaturadasException(
                "Hay demasiadas ingestas en curso o en espera; reintentar mas tarde.", _segundosParaReintentar);
        }

        try
        {
            await _enCurso.WaitAsync(cancellationToken);
        }
        catch
        {
            Interlocked.Decrement(ref _admitidas);
            throw;
        }

        return new Permiso(this);
    }

    private sealed class Permiso(LimiteDeIngestas limite) : IDisposable
    {
        private int _liberado;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _liberado, 1) != 0)
                return;
            limite._enCurso.Release();
            Interlocked.Decrement(ref limite._admitidas);
        }
    }
}

/// <summary>Ingesta rechazada por el limite de admision (429, con Retry-After).</summary>
public sealed class IngestasSaturadasException(string message, int segundosParaReintentar)
    : HttpException(message, HttpStatusCode.TooManyRequests)
{
    public int SegundosParaReintentar { get; } = segundosParaReintentar;
}
