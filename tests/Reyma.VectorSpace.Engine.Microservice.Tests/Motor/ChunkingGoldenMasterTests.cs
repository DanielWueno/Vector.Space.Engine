using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Chunking;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Utilities;
using Xunit;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Golden master del chunking: congela la salida exacta de las 4 estrategias sobre
/// un corpus de fixtures inmutables.
///
/// Porte (item 1.4): fixtures y GoldenMaster/chunking.json copiados SIN CAMBIOS de
/// Rag.Context.Engine (tests/RagEngine.Core.Tests, commit aae1de8). Que este test pase
/// aqui prueba que el chunker portado produce los mismos chunks y content_hash que el
/// de origen; no se regenera el golden master en este repo (ver PORTE.md).
///
/// Para qué sirve: el ítem 2.1 del plan extrae la base común de las 4 estrategias,
/// que hoy duplican el encabezado semántico, la ventana deslizante, la agrupación
/// por presupuesto de tokens y la construcción de CodeChunk. Un refactor así no se
/// puede verificar con una corrida de recall — el recall es un agregado que puede
/// promediar diferencias reales, y medirlo sobre bsuite-repo cuesta ~19 h porque
/// cambiar los chunks invalida los 18.784 resúmenes cacheados.
///
/// Este test es evidencia más fuerte y cuesta milisegundos: si el refactor preserva
/// el comportamiento, la salida es byte-idéntica; si cambia algo, dice exactamente
/// qué fixture, qué chunk y qué campo.
///
/// Los fixtures vienen en pares LF/CRLF del MISMO contenido a propósito: las
/// estrategias de C# y Fallback parten con Split('\n') mientras TypeScript y
/// Markdown usan Split(["\r\n","\r","\n"]), así que en archivos CRLF las dos
/// primeras dejan '\r' dentro del Content y del ContentHash. El golden master
/// documenta ese comportamiento HOY para que el refactor que lo unifique muestre
/// exactamente qué cambia.
/// </summary>
public class ChunkingGoldenMasterTests
{
    private sealed record ChunkSnapshot(
        string Type,
        string? ClassName,
        string? MethodName,
        int StartLine,
        int EndLine,
        string ContentHash,
        int ContentLength,
        bool ContentTieneCr);

    private static readonly (string Fixture, SourceLanguage Language)[] Casos =
    [
        ("csharp-sample.fixture",         SourceLanguage.CSharp),
        ("csharp-sample-crlf.fixture",    SourceLanguage.CSharp),
        ("typescript-sample.fixture",     SourceLanguage.TypeScript),
        ("typescript-sample-crlf.fixture", SourceLanguage.TypeScript),
        ("markdown-sample.fixture",       SourceLanguage.Markdown),
        ("markdown-sample-crlf.fixture",  SourceLanguage.Markdown),
        ("plaintext-sample.fixture",      SourceLanguage.PlainText),
        ("plaintext-sample-crlf.fixture", SourceLanguage.PlainText),
        // Fixtures que EXCEDEN MaxTokensPerChunk a proposito: sin ellos el golden
        // master no cubria la agrupacion de parrafos por presupuesto de tokens, que
        // solo se activa cuando un bloque no cabe. Refactorizar ese camino sin estos
        // casos habria sido a ciegas.
        ("markdown-large.fixture",        SourceLanguage.Markdown),
        ("markdown-large-crlf.fixture",   SourceLanguage.Markdown),
        ("typescript-large.fixture",      SourceLanguage.TypeScript),
        ("typescript-large-crlf.fixture", SourceLanguage.TypeScript),
    ];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private static IChunkingStrategy StrategyFor(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp => new RoslynCSharpChunkingStrategy(
            NullLogger<RoslynCSharpChunkingStrategy>.Instance),
        SourceLanguage.TypeScript => new TypeScriptChunkingStrategy(
            NullLogger<TypeScriptChunkingStrategy>.Instance),
        SourceLanguage.Markdown => new MarkdownChunkingStrategy(
            NullLogger<MarkdownChunkingStrategy>.Instance),
        _ => new FallbackChunkingStrategy(),
    };

    private static async Task<List<ChunkSnapshot>> SnapshotAsync(string fixture, SourceLanguage language)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        // newline: false — se lee el archivo tal cual, sin que .NET normalice nada.
        var contenido = File.ReadAllText(ruta);

        var artifact = new RawArtifact(
            AbsolutePath: $"/fixtures/{fixture}",
            RelativePath: fixture,
            Language: language,
            LastModified: DateTimeOffset.UnixEpoch,
            SizeBytes: contenido.Length);

        var snapshots = new List<ChunkSnapshot>();
        await foreach (var chunk in StrategyFor(language)
            .ChunkAsync(artifact, contenido, ChunkingOptions.Default))
        {
            snapshots.Add(new ChunkSnapshot(
                chunk.Type.ToString(),
                chunk.Metadata.ClassName,
                chunk.Metadata.MethodName,
                chunk.Metadata.StartLine,
                chunk.Metadata.EndLine,
                chunk.ContentHash,
                chunk.Content.Length,
                chunk.Content.Contains('\r')));
        }

        return snapshots;
    }

    [Fact]
    public async Task LaSalidaDeLosChunkers_NoCambioRespectoAlGoldenMaster()
    {
        var actual = new Dictionary<string, List<ChunkSnapshot>>();
        foreach (var (fixture, language) in Casos)
        {
            actual[fixture] = await SnapshotAsync(fixture, language);
        }

        var rutaEnOutput = Path.Combine(AppContext.BaseDirectory, "GoldenMaster", "chunking.json");

        // Porte: el golden master es el de Rag.Context.Engine y no se genera aqui. Sin el,
        // el test no puede decir nada: falla.
        Assert.True(File.Exists(rutaEnOutput), $"Falta el golden master portado en {rutaEnOutput}.");

        var esperado = JsonSerializer.Deserialize<Dictionary<string, List<ChunkSnapshot>>>(
            await File.ReadAllTextAsync(rutaEnOutput), JsonOpts)!;

        var diferencias = new List<string>();

        foreach (var (fixture, _) in Casos)
        {
            if (!esperado.TryGetValue(fixture, out var previos))
            {
                diferencias.Add($"{fixture}: no está en el golden master");
                continue;
            }

            var ahora = actual[fixture];

            if (previos.Count != ahora.Count)
            {
                diferencias.Add(
                    $"{fixture}: cambió el NÚMERO de chunks — antes {previos.Count}, ahora {ahora.Count}");
                continue;
            }

            for (int i = 0; i < ahora.Count; i++)
            {
                if (previos[i] != ahora[i])
                {
                    diferencias.Add($"{fixture} chunk[{i}]:\n     antes: {previos[i]}\n     ahora: {ahora[i]}");
                }
            }
        }

        Assert.True(diferencias.Count == 0,
            "La salida del chunking cambió respecto al golden master. Si el cambio es "
          + "INTENCIONAL, regenera el archivo y explica en el commit qué se movió y por qué. "
          + "Si no lo es, es una regresión:\n  - " + string.Join("\n  - ", diferencias));
    }

    [Fact]
    public async Task ElMismoContenidoEnLfYCrlf_DeberiaProducirElMismoContentHash()
    {
        // Un archivo con el mismo código no debería indexarse distinto por venir de
        // Windows. Hoy esto NO se cumple en C# ni en PlainText, porque esas
        // estrategias parten con Split('\n') y dejan el '\r' dentro del contenido.
        // Se deja Skip hasta el refactor del ítem 2.1, que unifica el split.
        var lf = await SnapshotAsync("csharp-sample.fixture", SourceLanguage.CSharp);
        var crlf = await SnapshotAsync("csharp-sample-crlf.fixture", SourceLanguage.CSharp);

        Assert.Equal(lf.Count, crlf.Count);
        Assert.Equal(
            lf.Select(c => c.ContentHash).ToArray(),
            crlf.Select(c => c.ContentHash).ToArray());
    }
}
