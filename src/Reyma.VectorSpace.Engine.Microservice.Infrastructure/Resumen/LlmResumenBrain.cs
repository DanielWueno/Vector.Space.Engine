using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Business;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;

/// <summary>Resultado de resumir un chunk (adaptado de <c>BusinessSummaryResult</c>, Rag.Context.Engine).</summary>
public sealed record ResumenLlmResultado(string Texto, bool SinContenidoDeNegocio, int TokensConsumidos);

/// <summary>
/// Genera el resumen de negocio de un chunk por la ruta <c>chat/completions</c> OpenAI-compatible de AltheIA (1.7.7).
/// Prompt, sentinel y parametros de muestreo son los de <c>OllamaBusinessSummaryGenerator</c> de Rag.Context.Engine
/// (ya validados en 1.7.3 contra AltheIA real), portados tal cual; el transporte es un <see cref="HttpClient"/>
/// nombrado, no Semantic Kernel, igual patron que <c>TeiVectorizationBrain</c>.
/// </summary>
public interface ILlmResumenBrain
{
    string ModelName { get; }

    /// <summary>Version del prompt vigente (hash de prompt+modelo): clave de cache junto con el content_hash del chunk.</summary>
    string PromptVersion { get; }

    /// <summary>
    /// Null si el modelo respondio vacio (fallo de contenido aislado a este chunk, recuperable en una corrida
    /// posterior: ver <see cref="ResumenPipeline"/>). Un perfil mal configurado (modelo no permitido para la clave,
    /// o de otra capacidad) lanza <see cref="BadRequestException"/>, nunca 503.
    /// </summary>
    Task<ResumenLlmResultado?> ResumirAsync(string lenguaje, string rutaRelativa, string contenido, CancellationToken ct);
}

public sealed class LlmResumenBrain : ILlmResumenBrain
{
    public const string HttpClientName = "motor-llm-resumen";

    /// <summary>Mismo centinela que <c>OllamaBusinessSummaryGenerator.Sentinel</c> (Rag.Context.Engine).</summary>
    public const string Sentinel = "SIN_CONTENIDO_DE_NEGOCIO";

    private const string VersionBase = "v1";

    // Prompt de sistema literal de OllamaBusinessSummaryGenerator (Rag.Context.Engine), ya validado en 1.7.3 contra
    // AltheIA real (30/30 con qwen3.8-27b-fast). No se traduce ni se reformula: cambiarlo cambia PromptVersion y,
    // por tanto, invalida la cache de resumenes de todos los chunks.
    private const string SystemPrompt = """
        Eres un analista de negocio que traduce fragmentos de código — en cualquier lenguaje de
        programación — a descripciones de comportamiento para usuarios sin conocimiento técnico.

        No expliques sintaxis, nombres de frameworks ni construcciones del lenguaje. A partir de tu
        propio conocimiento de cómo funciona el lenguaje indicado en el contexto, identifica QUÉ hace
        este fragmento, QUÉ dato o acción involucra, y QUÉ regla o condición aplica — en el lenguaje
        que usaría alguien que jamás vio código.

        Responde en español, en 1 a 3 oraciones (máximo 60 palabras). Empieza nombrando la entidad,
        pantalla o archivo, seguido de dos puntos — sin frases como "este componente" o "esta clase
        representa". No inventes campos, reglas o comportamientos que no estén explícitos en el
        fragmento: si no está en el código, no existe.

        Si el fragmento es puramente técnico sin significado de negocio visible (imports,
        configuración, boilerplate, getters/setters triviales), responde exactamente:
        SIN_CONTENIDO_DE_NEGOCIO
        """;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmOptions _options;
    private readonly ILogger<LlmResumenBrain> _logger;
    private HttpClient? _http;

    /// <summary>Perfil por defecto: resuelve el HttpClient de forma perezosa (ver <see cref="Http"/>).</summary>
    public LlmResumenBrain(IHttpClientFactory httpClientFactory, IOptions<MotorOptions> options, ILogger<LlmResumenBrain> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.Llm;
        _logger = logger;
    }

    /// <summary>Puerta de prueba: HttpClient ya construido (sidecar simulado), igual patron que TeiVectorizationBrain.</summary>
    internal LlmResumenBrain(HttpClient http, LlmOptions options, ILogger<LlmResumenBrain> logger)
    {
        _httpClientFactory = null!;
        _options = options;
        _logger = logger;
        _http = http;
    }

    /// <summary>
    /// Perezoso a proposito: Motor:Llm:UrlBase puede estar vacio cuando el resumen no esta configurado en esta
    /// instancia (opt-in, 1.7.7). Si <see cref="CreateClient"/> corriera en el constructor, construir MotorVectorial
    /// (que se resuelve en TODA solicitud de ingesta/busqueda/embeddings, use o no resumen) fallaria con esa
    /// configuracion por defecto. Solo <see cref="ResumirAsync"/> -llamado unicamente cuando una coleccion ya tiene
    /// el resumen habilitado- fuerza la creacion real del HttpClient.
    /// </summary>
    private HttpClient Http => _http ??= _httpClientFactory.CreateClient(HttpClientName);

    public string ModelName => _options.Modelo;

    public string PromptVersion => ComputePromptVersion(_options.Modelo);

    internal static string ComputePromptVersion(string modelo)
    {
        var input = $"{VersionBase}|{modelo}|{SystemPrompt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    public async Task<ResumenLlmResultado?> ResumirAsync(string lenguaje, string rutaRelativa, string contenido, CancellationToken ct)
    {
        var userPrompt = $"[Contexto: Lenguaje: {lenguaje}, Ruta: {rutaRelativa}]\n\n{contenido}";
        var mensajes = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new { role = "user", content = userPrompt }
        };
        var chatTemplateKwargs = _options.EnableThinking is null ? null : (object)new { enable_thinking = _options.EnableThinking.Value };
        var request = new ChatRequest(_options.Modelo, mensajes, _options.Temperatura, _options.TopP, _options.MaxTokens, chatTemplateKwargs);

        ChatResponse? body;
        try
        {
            using var response = await Http.PostAsJsonAsync(_options.Ruta, request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                if (EsModeloNoDisponible(detail))
                {
                    _logger.LogWarning("El perfil de LLM rechazo el modelo '{Modelo}' ({Status}): {Detail}",
                        _options.Modelo, (int)response.StatusCode, Truncate(detail));
                    throw new BadRequestException(
                        $"El modelo '{_options.Modelo}' del perfil de LLM no esta disponible en el servidor remoto.");
                }
                _logger.LogError("El perfil de LLM respondio {Status}: {Detail}", (int)response.StatusCode, Truncate(detail));
                throw new MotorNoDisponibleException($"El perfil de LLM respondio {(int)response.StatusCode}.");
            }
            body = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "No se pudo llamar al perfil de LLM.");
            throw new MotorNoDisponibleException("El perfil de LLM no esta disponible.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "El perfil de LLM no respondio a tiempo.");
            throw new MotorNoDisponibleException("El perfil de LLM no respondio a tiempo.");
        }

        var texto = (body?.Choices?.FirstOrDefault()?.Message?.Content ?? "").Trim();
        var tokens = body?.Usage?.TotalTokens ?? 0;
        if (texto.Length == 0)
        {
            // Igual que el origen (OllamaBusinessSummaryGenerator): respuesta vacia es un fallo de contenido aislado
            // a este chunk (p. ej. un modelo 'deep' sin enable_thinking=false agotando MaxTokens razonando), no una
            // caida de dependencia. El chunk queda pendiente para la siguiente corrida.
            _logger.LogWarning("El perfil de LLM devolvio una respuesta vacia al resumir '{Ruta}' ({Tokens} tokens).", rutaRelativa, tokens);
            return null;
        }

        var sinNegocio = texto.StartsWith(Sentinel, StringComparison.Ordinal);
        return new ResumenLlmResultado(texto, sinNegocio, tokens);
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];

    /// <summary>Mismos codigos que AltheIA para embeddings (1.7.3/1.7.4), aplicables igual a chat-completions.</summary>
    private static bool EsModeloNoDisponible(string detail) =>
        detail.Contains("model_not_allowed", StringComparison.OrdinalIgnoreCase) ||
        detail.Contains("model_capability_mismatch", StringComparison.OrdinalIgnoreCase);

    internal sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] object[] Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("top_p")] double TopP,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("chat_template_kwargs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? ChatTemplateKwargs);

    internal sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices,
        [property: JsonPropertyName("usage")] ChatUsage? Usage);

    internal sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage? Message);

    internal sealed record ChatMessage([property: JsonPropertyName("content")] string? Content);

    internal sealed record ChatUsage([property: JsonPropertyName("total_tokens")] int TotalTokens);
}
