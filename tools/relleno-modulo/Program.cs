using System.Globalization;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;

// 1.21.1: relleno explicito de module_prefixes (o verificacion de solo lectura). Codigos de salida:
// 0 completo/verificado completo; 1 verificacion: incompleta; 2 argumentos; 3 Qdrant no disponible o error de
// comunicacion; 4 error inesperado; 10 + RellenoModuloError (11 coleccion inexistente, 12 lease ocupado, 13 lease perdido, 14 indice
// incompatible, 15 incompleto, 16 sin progreso, 17 payload inesperado); 130 cancelado.
const string Uso = """
    Uso: relleno-modulo --coleccion NOMBRE [--verificar] [--host 127.0.0.1] [--puerto-grpc 6334] [--puerto-rest 6333]
                        [--https] [--api-key-env VARIABLE] [--lote 256] [--timeout-segundos 60]
    --verificar   Solo lectura: marca, indices y puntos sin preparar. No escribe nada.
    --api-key-env Nombre de la variable de entorno con la api-key de Qdrant (la clave nunca se imprime).
    """;

string? coleccion = null, apiKeyEnv = null;
var host = "127.0.0.1";
int puertoGrpc = 6334, puertoRest = 6333, lote = RellenoDePrefijosDeModulo.LotePorDefecto, timeout = 60;
bool https = false, verificar = false;

try
{
    for (var i = 0; i < args.Length; i++)
    {
        string Valor() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Falta el valor de {args[i]}.");
        int Entero() => int.Parse(Valor(), NumberStyles.None, CultureInfo.InvariantCulture);
        switch (args[i])
        {
            case "--coleccion": coleccion = Valor(); break;
            case "--host": host = Valor(); break;
            case "--puerto-grpc": puertoGrpc = Entero(); break;
            case "--puerto-rest": puertoRest = Entero(); break;
            case "--https": https = true; break;
            case "--api-key-env": apiKeyEnv = Valor(); break;
            case "--lote": lote = Entero(); break;
            case "--timeout-segundos": timeout = Entero(); break;
            case "--verificar": verificar = true; break;
            case "-h" or "--help": Console.WriteLine(Uso); return 0;
            default: throw new ArgumentException($"Argumento desconocido: {args[i]}.");
        }
    }
    if (string.IsNullOrWhiteSpace(coleccion))
        throw new ArgumentException("--coleccion es obligatorio.");
    if (lote is < 1 or > 10_000 || timeout < 1 || puertoGrpc is < 1 or > 65535 || puertoRest is < 1 or > 65535)
        throw new ArgumentException("Lote (1..10000), timeout (>0) o puertos fuera de rango.");
}
catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(Uso);
    return 2;
}

string? apiKey = null;
if (apiKeyEnv is not null)
{
    apiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
    if (string.IsNullOrEmpty(apiKey))
    {
        Console.Error.WriteLine($"La variable de entorno '{apiKeyEnv}' no existe o esta vacia.");
        return 2;
    }
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var qdrant = new QdrantClient(host, puertoGrpc, https, apiKey, TimeSpan.FromSeconds(timeout));
using var rest = new HttpClient
{
    BaseAddress = new Uri($"{(https ? "https" : "http")}://{host}:{puertoRest}/"),
    Timeout = TimeSpan.FromSeconds(timeout)
};
if (apiKey is not null)
    rest.DefaultRequestHeaders.Add("api-key", apiKey);

var relleno = new RellenoDePrefijosDeModulo(qdrant, rest, new ConsolaLogger());

try
{
    if (verificar)
    {
        var e = await relleno.VerificarAsync(coleccion!, cts.Token);
        Console.WriteLine(
            $"coleccion={coleccion} completa={e.Completa} marca={e.Marca ?? "(ninguna)"} indice_prefijos={e.IndicePrefijos} " +
            $"indice_version={e.IndiceVersion} conflicto_indice={e.ConflictoIndice ?? "-"} puntos_sin_preparar={e.PuntosSinPreparar}");
        return e.Completa ? 0 : 1;
    }

    var r = await relleno.EjecutarAsync(coleccion!, lote, cts.Token);
    var estado = await relleno.VerificarAsync(coleccion!, cts.Token);
    Console.WriteLine(
        $"coleccion={coleccion} ya_completa={r.YaCompleta} puntos_actualizados={r.PuntosActualizados} paginas={r.Paginas} " +
        $"completa={estado.Completa} puntos_sin_preparar={estado.PuntosSinPreparar} marca={estado.Marca ?? "(ninguna)"}");
    if (!estado.Completa)
    {
        Console.Error.WriteLine("La verificacion posterior no confirma la coleccion completa (escritor concurrente?); relanzar.");
        return 10 + (int)RellenoModuloError.Incompleto;
    }
    return 0;
}
catch (RellenoModuloException ex)
{
    Console.Error.WriteLine($"{ex.Codigo}: {ex.Message}");
    return 10 + (int)ex.Codigo;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelado: la coleccion queda en curso (lease expirado), nunca completa. Relanzar para continuar.");
    return 130;
}
catch (Exception ex) when (ex is HttpRequestException or Grpc.Core.RpcException or TaskCanceledException)
{
    Console.Error.WriteLine($"Qdrant no disponible o error de comunicacion: {ex.GetType().Name}: {ex.Message}");
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error inesperado (la coleccion nunca queda marcada completa por el): {ex.GetType().Name}: {ex.Message}");
    return 4;
}

internal sealed class ConsolaLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        Console.Error.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception is not null && logLevel >= LogLevel.Warning)
            Console.Error.WriteLine($"  {exception.GetType().Name}: {exception.Message}");
    }
}
