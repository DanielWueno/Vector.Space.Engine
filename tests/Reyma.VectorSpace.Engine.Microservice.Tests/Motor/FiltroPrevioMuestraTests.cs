using System.Text.Json;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Xunit.Abstractions;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Criterio de aceptacion de 1.27, fijado en la ficha antes de medir, contra la muestra de 100 chunks de reyoneweb del
/// experimento resumen-fast-vs-deep (docs/evaluaciones/resumen-fast-vs-deep): el filtro POR DEFECTO excluye al menos 8
/// de los 9 chunks tecnicos que deep con pensamiento resumio de mas y 0 de los chunks donde ambos jueces (j3 y j4) vieron
/// negocio. Usa el codigo de produccion, no una reimplementacion. Si falla el 0 de negocio, se reduce la lista de
/// patrones, no el criterio.
/// </summary>
public sealed class FiltroPrevioMuestraTests(ITestOutputHelper salida)
{
    /// <summary>Informe del experimento, seccion "De donde sale el deficit de utilidad".</summary>
    private static readonly string[] NueveTecnicos = ["Q017", "Q024", "Q044", "Q046", "Q049", "Q067", "Q069", "Q088", "Q093"];

    private sealed record Chunk(string Par, string Ruta, string Tipo, string Contenido, bool NegocioJ3, bool NegocioJ4);

    private static string Experimento()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Reyma.VectorSpace.Engine.Microservice.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "docs", "evaluaciones", "resumen-fast-vs-deep");
    }

    private static JsonElement Leer(string ruta) => JsonDocument.Parse(File.ReadAllText(ruta)).RootElement;

    private static List<Chunk> Muestra()
    {
        var baseDir = Experimento();
        var porId = Leer(Path.Combine(baseDir, "privado", "muestra.json")).EnumerateArray()
            .ToDictionary(m => m.GetProperty("id").GetString()!);
        var j3 = Leer(Path.Combine(baseDir, "jueces2", "juicio-j3.json")).EnumerateArray()
            .ToDictionary(j => j.GetProperty("par").GetString()!, j => j.GetProperty("negocio_visible").GetBoolean());
        var j4 = Leer(Path.Combine(baseDir, "jueces2", "juicio-j4.json")).EnumerateArray()
            .ToDictionary(j => j.GetProperty("par").GetString()!, j => j.GetProperty("negocio_visible").GetBoolean());

        return Leer(Path.Combine(baseDir, "privado", "mapa-thinking.json")).EnumerateObject().Select(par =>
        {
            var m = porId[par.Value.GetProperty("id").GetString()!];
            return new Chunk(par.Name, m.GetProperty("relative_path").GetString()!, m.GetProperty("chunk_type").GetString()!,
                m.GetProperty("content").GetString()!, j3[par.Name], j4[par.Name]);
        }).ToList();
    }

    [Fact]
    public void ElFiltroPorDefectoCumpleElCriterioSobreLaMuestraJuzgada()
    {
        var filtro = new FiltroPrevioDeResumen(new FiltroPrevioOptions());
        var muestra = Muestra();
        Assert.Equal(100, muestra.Count);

        var evaluados = muestra.Select(c => (Chunk: c, Motivo: filtro.Motivo(c.Ruta, c.Tipo, c.Contenido))).ToList();
        var excluidos = evaluados.Where(e => e.Motivo is not null).ToList();
        var nueveExcluidos = excluidos.Count(e => NueveTecnicos.Contains(e.Chunk.Par));
        var negocio = evaluados.Where(e => e.Chunk.NegocioJ3 && e.Chunk.NegocioJ4).ToList();
        var sinNegocio = evaluados.Where(e => !e.Chunk.NegocioJ3 && !e.Chunk.NegocioJ4).ToList();

        salida.WriteLine($"Excluidos: {excluidos.Count}/100");
        salida.WriteLine($"De los 9 tecnicos: {nueveExcluidos}/9; fuera: " +
                         string.Join(",", NueveTecnicos.Where(q => !excluidos.Any(e => e.Chunk.Par == q))));
        salida.WriteLine($"Negocio para ambos jueces excluido: {negocio.Count(e => e.Motivo is not null)}/{negocio.Count}");
        salida.WriteLine($"Sin negocio para ambos jueces excluido: {sinNegocio.Count(e => e.Motivo is not null)}/{sinNegocio.Count}");
        foreach (var grupo in excluidos.GroupBy(e => e.Motivo).OrderBy(g => g.Key))
            salida.WriteLine($"  {grupo.Key}: {grupo.Count()} ({string.Join(",", grupo.Select(e => e.Chunk.Par))})");

        Assert.True(nueveExcluidos >= 8, $"Excluye {nueveExcluidos} de los 9 tecnicos; el criterio pide al menos 8.");
        Assert.Empty(negocio.Where(e => e.Motivo is not null).Select(e => e.Chunk.Par));
    }
}
