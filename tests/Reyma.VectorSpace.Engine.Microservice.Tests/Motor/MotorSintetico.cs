using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Reyma.Exceptions.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Solo con <c>VSE_QDRANT_LOCAL=1</c> y un Qdrant local en 127.0.0.1:16333/16334 (item 1.21.1). A diferencia de
/// <see cref="RequiereMotorLocalFactAttribute"/>, no necesita TEI ni LLM: embeddings y resumen se inyectan
/// (<see cref="FabricaDeVectoresSintetica"/>, <see cref="LlmSintetico"/>).
/// </summary>
public sealed class RequiereQdrantLocalFactAttribute : FactAttribute
{
    public const string Variable = "VSE_QDRANT_LOCAL";

    public RequiereQdrantLocalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"Requiere {Variable}=1 y un Qdrant local (127.0.0.1:16333 REST, 16334 gRPC).";
    }
}

/// <summary>Contador compartido de llamadas a embeddings y LLM sinteticos.</summary>
public sealed class ContadorSintetico
{
    private int _embeddings;
    private int _llm;
    public int Embeddings => Volatile.Read(ref _embeddings);
    public int Llm => Volatile.Read(ref _llm);
    public void SumarEmbeddings() => Interlocked.Increment(ref _embeddings);
    public void SumarLlm() => Interlocked.Increment(ref _llm);
}

/// <summary>
/// Embeddings deterministas sin red: la consulta es el eje e0 y un documento con el marcador <c>score=0.NN</c> es
/// <c>[s, sqrt(1-s^2), 0, ...]</c>, asi que su coseno con cualquier consulta es exactamente <c>s</c>. Sin marcador,
/// <see cref="ScoreSinMarcador"/>. Permite fijar que puntos puntuan mas alto sin depender de un modelo.
/// </summary>
public sealed partial class VectoresSinteticos(string modelo, int dimension, ContadorSintetico contador) : IVectorizationBrain
{
    public const float ScoreSinMarcador = 0.05f;

    public string ModelName => modelo;
    public int EmbeddingDimensions => dimension;

    [GeneratedRegex(@"score=(0\.\d+)")]
    private static partial Regex Marcador();

    public static float ScoreDe(string texto)
    {
        var m = Marcador().Match(texto);
        return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : ScoreSinMarcador;
    }

    public float[] Vector(float s)
    {
        var v = new float[dimension];
        v[0] = s;
        v[1] = MathF.Sqrt(1f - s * s);
        return v;
    }

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        contador.SumarEmbeddings();
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => Vector(ScoreDe(t))).ToList());
    }

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        contador.SumarEmbeddings();
        var v = new float[dimension];
        v[0] = 1f;
        return Task.FromResult(v);
    }
}

/// <summary>Fabrica que resuelve el perfil por defecto y los adicionales declarados con <see cref="VectoresSinteticos"/>.</summary>
public sealed class FabricaDeVectoresSintetica(IOptions<MotorOptions> options, ContadorSintetico contador) : IVectorizationBrainFactory
{
    public string PerfilPorDefecto => MotorOptions.PerfilEmbeddingsPorDefecto;

    public IReadOnlyCollection<string> NombresDisponibles =>
        [PerfilPorDefecto, .. options.Value.Embeddings.Perfiles.Keys];

    public PerfilVectorizacionResuelto Resolver(string? perfilSolicitado)
    {
        if (string.IsNullOrWhiteSpace(perfilSolicitado) || perfilSolicitado == PerfilPorDefecto)
            return new(PerfilPorDefecto, new VectoresSinteticos("sintetico-codigo", options.Value.Embeddings.Dimension, contador));
        if (!options.Value.Embeddings.Perfiles.TryGetValue(perfilSolicitado, out var perfil))
            throw new BadRequestException($"El perfil de embeddings '{perfilSolicitado}' no esta registrado.");
        return new(perfilSolicitado, new VectoresSinteticos("sintetico-" + perfilSolicitado, perfil.Dimension, contador));
    }
}

/// <summary>LLM sin red: el resumen conserva el marcador de score del contenido, para controlar el espacio resumen.</summary>
public sealed class LlmSintetico(ContadorSintetico contador) : ILlmResumenBrain
{
    public string ModelName => "llm-sintetico";
    public string PromptVersion => "prompt-sintetico-v1";

    public Task<ResumenLlmResultado?> ResumirAsync(string lenguaje, string rutaRelativa, string contenido, CancellationToken ct)
    {
        contador.SumarLlm();
        var score = VectoresSinteticos.ScoreDe(contenido).ToString("0.00", CultureInfo.InvariantCulture);
        return Task.FromResult<ResumenLlmResultado?>(new ResumenLlmResultado($"Resumen de {rutaRelativa} score={score}", false, 10));
    }
}
