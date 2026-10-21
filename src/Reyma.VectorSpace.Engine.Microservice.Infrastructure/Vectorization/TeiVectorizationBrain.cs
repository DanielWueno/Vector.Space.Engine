using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

/// <summary>
/// Cliente de la ruta OpenAI-compatible del sidecar (1.8). No recorta el texto: TEI trunca solo por encima de la
/// ventana del modelo con --auto-truncate (1.11). Pide <c>dimensions</c> y ademas trunca y normaliza L2 en el cliente,
/// porque Infinity trunca sin renormalizar (1.8); con TEI la segunda normalizacion no cambia nada.
/// </summary>
public sealed class TeiVectorizationBrain : IVectorizationBrain
{
    public const string HttpClientName = "motor-embeddings";

    private readonly HttpClient _http;
    private readonly EmbeddingsOptions _options;
    private readonly ILogger<TeiVectorizationBrain> _logger;

    public TeiVectorizationBrain(IHttpClientFactory httpClientFactory, IOptions<MotorOptions> options, ILogger<TeiVectorizationBrain> logger)
    {
        _http = httpClientFactory.CreateClient(HttpClientName);
        _options = options.Value.Embeddings;
        _logger = logger;
    }

    public string ModelName => _options.Modelo;

    public int EmbeddingDimensions => _options.Dimension;

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var vectors = new List<float[]>(texts.Count);
        for (var i = 0; i < texts.Count; i += _options.TextosPorLlamada)
        {
            var slice = texts.Skip(i).Take(_options.TextosPorLlamada).ToArray();
            vectors.AddRange(await EmbedAsync(slice, cancellationToken));
        }
        return vectors;
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
        (await EmbedAsync([_options.PrefijoConsulta + text], cancellationToken))[0];

    private async Task<float[][]> EmbedAsync(string[] input, CancellationToken cancellationToken)
    {
        var request = new EmbeddingsRequest(input, _options.Modelo, _options.Dimension);
        EmbeddingsResponse? body;
        try
        {
            using var response = await _http.PostAsJsonAsync(_options.Ruta, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("El sidecar de embeddings respondio {Status} a {Count} textos: {Detail}",
                    (int)response.StatusCode, input.Length, Truncate(detail));
                throw new MotorNoDisponibleException($"El sidecar de embeddings respondio {(int)response.StatusCode}.");
            }
            body = await response.Content.ReadFromJsonAsync<EmbeddingsResponse>(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "No se pudo llamar al sidecar de embeddings.");
            throw new MotorNoDisponibleException("El sidecar de embeddings no esta disponible.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "El sidecar de embeddings no respondio a tiempo ({Count} textos).", input.Length);
            throw new MotorNoDisponibleException("El sidecar de embeddings no respondio a tiempo.");
        }

        return ToVectors(body, input.Length, _options.Dimension);
    }

    /// <summary>
    /// Ordena por <c>index</c> y exige exactamente un vector por texto: un sidecar que devuelve menos, repetidos o fuera
    /// de rango desalinearia vectores y chunks sin error visible.
    /// </summary>
    internal static float[][] ToVectors(EmbeddingsResponse? body, int expected, int dimension)
    {
        var data = body?.Data ?? throw new MotorNoDisponibleException("El sidecar de embeddings devolvio una respuesta sin datos.");
        if (data.Count != expected || data.Select(d => d.Index).Distinct().Count() != expected || data.Any(d => d.Index < 0 || d.Index >= expected))
            throw new MotorNoDisponibleException($"El sidecar de embeddings devolvio {data.Count} vectores para {expected} textos.");

        var vectors = new float[expected][];
        foreach (var item in data)
        {
            var raw = item.Embedding ?? [];
            if (raw.Length < dimension)
                throw new MotorNoDisponibleException(
                    $"El sidecar de embeddings devolvio {raw.Length} dimensiones; la coleccion usa {dimension}.");
            vectors[item.Index] = Normalize(raw.AsSpan(0, dimension));
        }
        return vectors;
    }

    internal static float[] Normalize(ReadOnlySpan<float> values)
    {
        double sum = 0;
        foreach (var v in values)
            sum += (double)v * v;
        var norm = Math.Sqrt(sum);
        if (!double.IsFinite(norm) || norm == 0)
            throw new MotorNoDisponibleException("El sidecar de embeddings devolvio un vector nulo o no finito.");

        var result = new float[values.Length];
        for (var i = 0; i < values.Length; i++)
            result[i] = (float)(values[i] / norm);
        return result;
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];

    internal sealed record EmbeddingsRequest(
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("dimensions")] int Dimensions);

    internal sealed record EmbeddingsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingItem>? Data);

    internal sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
