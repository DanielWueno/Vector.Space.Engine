using System.Text.Json;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Scanning;
using Xunit.Abstractions;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Criterio de aceptacion de 1.27, fijado en la ficha antes de medir, contra la muestra de 100 chunks de reyoneweb del
/// experimento resumen-fast-vs-deep (docs/evaluaciones/resumen-fast-vs-deep): lo que no se resume excluye al menos 8 de
/// los 9 chunks tecnicos que deep con pensamiento resumio de mas y 0 de los chunks donde ambos jueces (j3 y j4) vieron
/// negocio. Desde la v2 del filtro (2026-11-17) "lo que no se resume" es la suma de dos piezas, como en una ingesta
/// real: el filtro generico del motor y el .ragignore del repo (docs/corpus/reyoneweb.ragignore), que saca del indice
/// los envoltorios de FastReport. Usa el codigo de produccion de ambas, no una reimplementacion.
/// </summary>
public sealed class FiltroPrevioMuestraTests(ITestOutputHelper salida)
{
    /// <summary>Informe del experimento, seccion "De donde sale el deficit de utilidad".</summary>
    private static readonly string[] NueveTecnicos = ["Q017", "Q024", "Q044", "Q046", "Q049", "Q067", "Q069", "Q088", "Q093"];

    private sealed record Chunk(string Par, string Ruta, string Tipo, string Contenido, bool NegocioJuez1, bool NegocioJuez2);

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Reyma.VectorSpace.Engine.Microservice.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static JsonElement Leer(string ruta) => JsonDocument.Parse(File.ReadAllText(ruta)).RootElement;

    private static Dictionary<string, bool> NegocioVisible(string juicio) => Leer(juicio).EnumerateArray()
        .ToDictionary(j => j.GetProperty("par").GetString()!, j => j.GetProperty("negocio_visible").GetBoolean());

    private static List<Chunk> Muestra(string experimento, string jueces, string juez1, string juez2, string mapa)
    {
        var baseDir = Path.Combine(Raiz(), "docs", "evaluaciones", experimento);
        var porId = Leer(Path.Combine(baseDir, "privado", "muestra.json")).EnumerateArray()
            .ToDictionary(m => m.GetProperty("id").GetString()!);
        var n1 = NegocioVisible(Path.Combine(baseDir, jueces, $"juicio-{juez1}.json"));
        var n2 = NegocioVisible(Path.Combine(baseDir, jueces, $"juicio-{juez2}.json"));

        return Leer(Path.Combine(baseDir, "privado", mapa)).EnumerateObject().Select(par =>
        {
            var m = porId[par.Value.GetProperty("id").GetString()!];
            return new Chunk(par.Name, m.GetProperty("relative_path").GetString()!, m.GetProperty("chunk_type").GetString()!,
                m.GetProperty("content").GetString()!, n1[par.Name], n2[par.Name]);
        }).ToList();
    }

    /// <summary>Motivo por el que un chunk no se resume: "ragignore", el del filtro del motor, o null si va al LLM.</summary>
    private static List<(Chunk Chunk, string? Motivo)> Evaluar(List<Chunk> muestra)
    {
        var ragignore = IgnoreRules.FromLines(File.ReadAllLines(Path.Combine(Raiz(), "docs", "corpus", "reyoneweb.ragignore")));
        var filtro = new FiltroPrevioDeResumen(new FiltroPrevioOptions());
        return muestra.Select(c => (c, ragignore.IsIgnored(c.Ruta, isDirectory: false)
            ? "ragignore"
            : filtro.Motivo(c.Ruta, c.Tipo, c.Contenido))).ToList();
    }

    private void Informar(List<(Chunk Chunk, string? Motivo)> evaluados)
    {
        var negocio = evaluados.Where(e => e.Chunk.NegocioJuez1 && e.Chunk.NegocioJuez2).ToList();
        var sinNegocio = evaluados.Where(e => !e.Chunk.NegocioJuez1 && !e.Chunk.NegocioJuez2).ToList();
        salida.WriteLine($"No se resumen: {evaluados.Count(e => e.Motivo is not null)}/{evaluados.Count}");
        salida.WriteLine($"Negocio para ambos jueces excluido: {negocio.Count(e => e.Motivo is not null)}/{negocio.Count}");
        salida.WriteLine($"Sin negocio para ambos jueces excluido: {sinNegocio.Count(e => e.Motivo is not null)}/{sinNegocio.Count}");
        foreach (var grupo in evaluados.Where(e => e.Motivo is not null).GroupBy(e => e.Motivo).OrderBy(g => g.Key))
            salida.WriteLine($"  {grupo.Key}: {grupo.Count()} ({string.Join(",", grupo.Select(e => e.Chunk.Par))})");
    }

    [Fact]
    public void FiltroYRagignoreCumplenElCriterioSobreLaMuestraJuzgada()
    {
        var evaluados = Evaluar(Muestra("resumen-fast-vs-deep", "jueces2", "j3", "j4", "mapa-thinking.json"));
        Assert.Equal(100, evaluados.Count);
        Informar(evaluados);

        var excluidos = evaluados.Where(e => e.Motivo is not null).Select(e => e.Chunk.Par).ToHashSet();
        var nueveExcluidos = NueveTecnicos.Count(excluidos.Contains);
        salida.WriteLine($"De los 9 tecnicos: {nueveExcluidos}/9; fuera: {string.Join(",", NueveTecnicos.Where(q => !excluidos.Contains(q)))}");

        Assert.True(nueveExcluidos >= 8, $"Excluye {nueveExcluidos} de los 9 tecnicos; el criterio pide al menos 8.");
        Assert.Empty(evaluados.Where(e => e.Chunk.NegocioJuez1 && e.Chunk.NegocioJuez2 && e.Motivo is not null).Select(e => e.Chunk.Par));
    }

    /// <summary>
    /// Re-prueba del 2026-11-17 (docs/evaluaciones/resumen-fast-vs-deep-filtrado), muestreada entre lo que el filtro v1
    /// dejaba pasar: el .ragignore saca los dos scripts de FastReport/ que ambos jueces vieron tecnicos (R080, R089) y
    /// ningun chunk con negocio para ambos.
    /// </summary>
    [Fact]
    public void ElRagignoreSacaLosScriptsDeFastReportDeLaReprueba()
    {
        var evaluados = Evaluar(Muestra("resumen-fast-vs-deep-filtrado", "jueces", "j5", "j6", "mapa.json"));
        Assert.Equal(100, evaluados.Count);
        Informar(evaluados);

        var excluidos = evaluados.Where(e => e.Motivo is not null).Select(e => e.Chunk.Par).ToList();
        Assert.Contains("R080", excluidos);
        Assert.Contains("R089", excluidos);
        Assert.Empty(evaluados.Where(e => e.Chunk.NegocioJuez1 && e.Chunk.NegocioJuez2 && e.Motivo is not null).Select(e => e.Chunk.Par));
    }
}
