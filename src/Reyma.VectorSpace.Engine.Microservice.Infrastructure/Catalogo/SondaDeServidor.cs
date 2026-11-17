using System.Net;
using System.Text.Json;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Catalogo;

/// <summary>Resultado de sondear el servidor de un perfil (embeddings o LLM).</summary>
internal sealed record ResultadoSonda(bool Disponible, bool ModeloVerificado, int? VentanaTokens, string? Detalle);

/// <summary>
/// Sondeo barato, sin gastar tokens, de si el servidor de un perfil responde y sirve su modelo. Usa el mismo
/// HttpClient nombrado del perfil (misma clave Bearer y misma CA adicional que las llamadas reales), por eso un 401
/// aqui es el mismo 401 que veria una ingesta. Prueba, en orden, lo que exponen los servidores conocidos:
/// <list type="number">
/// <item><c>GET .../models</c> OpenAI-compatible (AltheIA/vLLM, Infinity): lista los modelos que la credencial puede
/// usar, con su ventana (<c>max_model_len</c>); 1.7.3 midio 3-6 ms.</item>
/// <item><c>GET info</c> de TEI: el <c>model_id</c> que sirve y su <c>max_input_length</c>.</item>
/// <item><c>GET health</c>: solo dice que responde, sin poder verificar el modelo.</item>
/// </list>
/// Los mensajes nunca llevan host, puerto ni el cuerpo de la respuesta: el catalogo los devuelve al cliente.
/// </summary>
internal static class SondaDeServidor
{
    public static async Task<ResultadoSonda> SondearAsync(HttpClient http, string ruta, string modelo, TimeSpan plazo, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(plazo);
        try
        {
            using (var modelos = await http.GetAsync(RutaDeModelos(ruta), cts.Token))
            {
                if (modelos.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return new(false, false, null, $"El servidor rechazo la credencial del servicio ({(int)modelos.StatusCode}).");
                if (modelos.IsSuccessStatusCode && LeerModelos(await modelos.Content.ReadAsStringAsync(cts.Token)) is { } lista)
                {
                    var encontrado = lista.FirstOrDefault(m => string.Equals(m.Id, modelo, StringComparison.Ordinal));
                    return encontrado is not null
                        ? new(true, true, encontrado.Ventana, null)
                        : new(false, false, null,
                            $"El servidor no ofrece el modelo '{modelo}' a la credencial del servicio. Ofrece: {string.Join(", ", lista.Select(m => m.Id))}.");
                }
            }

            using (var info = await http.GetAsync("info", cts.Token))
            {
                if (info.IsSuccessStatusCode && LeerInfoTei(await info.Content.ReadAsStringAsync(cts.Token)) is { } tei)
                {
                    return string.Equals(tei.Id, modelo, StringComparison.OrdinalIgnoreCase)
                        ? new(true, true, tei.Ventana, null)
                        : new(true, false, tei.Ventana, $"El servidor responde, pero sirve '{tei.Id}' y el perfil declara '{modelo}'.");
                }
            }

            using var health = await http.GetAsync("health", cts.Token);
            return health.IsSuccessStatusCode
                ? new(true, false, null, "El servidor responde, pero no expone que modelo sirve.")
                : new(false, false, null, $"El servidor respondio {(int)health.StatusCode}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, false, null, $"El servidor no respondio en {plazo.TotalSeconds:0} s.");
        }
        catch (HttpRequestException)
        {
            return new(false, false, null, "El servidor no responde.");
        }
    }

    /// <summary>
    /// <c>v1/embeddings</c> y <c>v1/chat/completions</c> -> <c>v1/models</c>; <c>embeddings</c> (Infinity) -> <c>models</c>.
    /// </summary>
    internal static string RutaDeModelos(string ruta)
    {
        var limpia = ruta.TrimStart('/');
        var v1 = limpia.IndexOf("v1/", StringComparison.Ordinal);
        return (v1 >= 0 ? limpia[..(v1 + 3)] : "") + "models";
    }

    private sealed record Modelo(string Id, int? Ventana);

    /// <summary><c>{"data":[{"id":"bge-m3","max_model_len":7372}, ...]}</c>; null si el cuerpo no tiene esa forma.</summary>
    private static IReadOnlyList<Modelo>? LeerModelos(string cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;
            return data.EnumerateArray()
                .Where(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(m => new Modelo(m.GetProperty("id").GetString()!, Entero(m, "max_model_len")))
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>TEI: <c>{"model_id":"...","max_input_length":8192, ...}</c>; null si el cuerpo no tiene esa forma.</summary>
    private static Modelo? LeerInfoTei(string cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("model_id", out var id) && id.ValueKind == JsonValueKind.String
                ? new Modelo(id.GetString()!, Entero(doc.RootElement, "max_input_length"))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? Entero(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor) && valor.TryGetInt32(out var n) ? n : null;
}
