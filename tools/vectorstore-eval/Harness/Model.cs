namespace Vse.Eval;

/// <summary>
/// Un punto con la misma forma que escribe QdrantVectorStore.UpsertBatchAsync en Rag.Context.Engine:
/// denso, sparse lexico (indices &lt; 2^20, como SparseTokenizer), resumen opcional y el payload que
/// usan los filtros de QdrantSemanticRetriever.BuildFilter.
/// </summary>
public sealed record Chunk(
    Guid Id,
    float[] Dense,
    uint[] SparseIdx,
    float[] SparseVal,
    float[]? Summary,
    bool ResumenPending,
    string Content,
    string FilePath,
    string RelativePath,
    string Language,
    string? Namespace,
    string? ClassName,
    string? MethodName,
    int StartLine,
    int EndLine,
    string ChunkType,
    string ContentHash,
    string LastModified,
    string RepositoryName,
    string[] DefinedSymbols,
    string[] ConsumedSymbols,
    string? Tenant)
{
    public string Key => Id.ToString("D");
}

public sealed record EvalFilter(
    string Label,
    string? Tenant = null,
    string? Module = null,
    string? Language = null,
    IReadOnlyList<string>? DefinedAny = null,
    IReadOnlyList<(string Class, string Method)>? Pairs = null,
    IReadOnlyList<Guid>? Ids = null,
    bool ResumenPendingOnly = false);

public sealed record Hit(string Id, float Score);

public enum Branch { Dense, Sparse, Resumen }

public sealed record Query(int Index, float[] Dense, uint[] SparseIdx, float[] SparseVal);

public static class Dataset
{
    public const int Dim = 384;
    public const int TieGroupSize = 40;

    public static readonly string[] Namespaces =
    {
        "Reyma.Ventas.Api", "Reyma.Ventas.Domain", "Reyma.VentasExtra.Api", "Reyma.Compras.Api", "Reyma.Compras.Domain",
    };

    /// <summary>
    /// Dataset sintetico deterministico (System.Random con semilla es estable entre ejecuciones).
    /// Incluye un grupo de <see cref="TieGroupSize"/> puntos con vectores IDENTICOS (empate exacto) y
    /// 20 trios identicos, para ejercitar el prefijo completo de empates de DeterministicVectorQuery.
    /// </summary>
    public static (List<Chunk> Chunks, List<Query> Queries, float[] TieDense) Generate(int n, int seed = 12345)
    {
        var rnd = new Random(seed);
        var centroids = Enumerable.Range(0, 24).Select(_ => Normalize(Gaussian(rnd, Dim))).ToArray();
        var vocab = new HashSet<uint>();
        while (vocab.Count < 4000) vocab.Add((uint)rnd.Next(0, 1 << 20));
        var vocabArr = vocab.ToArray();
        var symbols = Enumerable.Range(0, 300).Select(i => $"Sym{i:000}").ToArray();
        var classes = Enumerable.Range(0, 50).Select(i => $"Class{i:00}").ToArray();

        var chunks = new List<Chunk>(n);
        for (int i = 0; i < n; i++)
        {
            int c = rnd.Next(centroids.Length);
            var dense = Normalize(Add(centroids[c], Scale(Gaussian(rnd, Dim), 0.6f)));
            var (sIdx, sVal) = Sparse(rnd, vocabArr, c);
            bool md = rnd.NextDouble() < 0.25;
            string? ns = md ? null : Namespaces[rnd.Next(Namespaces.Length)];
            string rel = md ? $"docs/guia-{i}.md" : $"src/{ns!.Replace('.', '/')}/File{i}.cs";
            double t = rnd.NextDouble();
            string? tenant = t < 0.4 ? "tenant-a" : t < 0.7 ? "tenant-b" : null;
            var defined = md ? Array.Empty<string>() : Pick(rnd, symbols, rnd.Next(0, 4));
            var consumed = md ? Array.Empty<string>() : Pick(rnd, symbols, rnd.Next(0, 6));
            bool hasSummary = rnd.NextDouble() < 0.6;
            float[]? summary = hasSummary ? Normalize(Add(dense, Scale(Gaussian(rnd, Dim), 0.8f))) : null;
            chunks.Add(new Chunk(
                NewGuid(rnd), dense, sIdx, sVal, summary, ResumenPending: !hasSummary,
                Content: $"contenido sintetico {i}", FilePath: "/repo/" + rel, RelativePath: rel,
                Language: md ? "Markdown" : "CSharp", Namespace: ns,
                ClassName: md ? null : classes[rnd.Next(classes.Length)],
                MethodName: md ? null : $"Metodo{rnd.Next(100)}",
                StartLine: i, EndLine: i + 20, ChunkType: md ? "Section" : "Method",
                ContentHash: $"h{i:x8}", LastModified: "2026-10-19T00:00:00.0000000+00:00",
                RepositoryName: "repo-sintetico", DefinedSymbols: defined, ConsumedSymbols: consumed, Tenant: tenant));
        }

        // Grupo de empate exacto: TieGroupSize puntos con el MISMO denso y el MISMO sparse.
        var tieDense = Normalize(Gaussian(rnd, Dim));
        var (tIdx, tVal) = Sparse(rnd, vocabArr, 0);
        for (int k = 0; k < TieGroupSize; k++)
        {
            int at = rnd.Next(chunks.Count);
            chunks[at] = chunks[at] with { Dense = tieDense, SparseIdx = tIdx, SparseVal = tVal };
        }
        // 20 trios identicos repartidos por la coleccion.
        for (int g = 0; g < 20; g++)
        {
            var src = chunks[rnd.Next(chunks.Count)];
            for (int k = 0; k < 2; k++)
            {
                int at = rnd.Next(chunks.Count);
                chunks[at] = chunks[at] with { Dense = src.Dense, SparseIdx = src.SparseIdx, SparseVal = src.SparseVal };
            }
        }

        var queries = new List<Query>();
        for (int q = 0; q < 30; q++)
        {
            float[] qd;
            uint[] qi;
            float[] qv;
            if (q < 20)
            {
                var src = chunks[rnd.Next(chunks.Count)];
                qd = Normalize(Add(src.Dense, Scale(Gaussian(rnd, Dim), 0.5f)));
                var take = Math.Max(1, src.SparseIdx.Length / 2);
                var (ri, rv) = Sparse(rnd, vocabArr, rnd.Next(24));
                var merged = new SortedDictionary<uint, float>();
                for (int k = 0; k < take; k++) merged[src.SparseIdx[k]] = 1f;
                for (int k = 0; k < Math.Min(3, ri.Length); k++) merged[ri[k]] = 1f;
                qi = merged.Keys.ToArray();
                qv = merged.Values.ToArray();
            }
            else
            {
                qd = Normalize(Add(centroids[rnd.Next(24)], Scale(Gaussian(rnd, Dim), 0.3f)));
                (qi, qv) = Sparse(rnd, vocabArr, rnd.Next(24));
            }
            queries.Add(new Query(q, qd, qi, qv));
        }
        return (chunks, queries, tieDense);
    }

    private static (uint[] Idx, float[] Val) Sparse(Random rnd, uint[] vocab, int cluster)
    {
        var tf = new SortedDictionary<uint, float>();
        int terms = rnd.Next(8, 31);
        for (int k = 0; k < terms; k++)
        {
            // sesgo por cluster: cada cluster tiene un "rango" preferente del vocabulario (Zipf aproximado).
            int baseOff = cluster * 150;
            int pos = rnd.NextDouble() < 0.7
                ? (baseOff + (int)(150 * Math.Pow(rnd.NextDouble(), 2))) % vocab.Length
                : rnd.Next(vocab.Length);
            var idx = vocab[pos];
            tf[idx] = tf.TryGetValue(idx, out var w) ? w + 1f : 1f;
        }
        return (tf.Keys.ToArray(), tf.Values.ToArray());
    }

    private static Guid NewGuid(Random rnd)
    {
        var b = new byte[16];
        rnd.NextBytes(b);
        b[7] = (byte)((b[7] & 0x0F) | 0x40);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b);
    }

    private static string[] Pick(Random rnd, string[] pool, int count)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        while (set.Count < count) set.Add(pool[rnd.Next(pool.Length)]);
        return set.ToArray();
    }

    public static float[] Gaussian(Random rnd, int dim)
    {
        var v = new float[dim];
        for (int i = 0; i < dim; i++)
        {
            double u1 = 1.0 - rnd.NextDouble(), u2 = rnd.NextDouble();
            v[i] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
        return v;
    }

    public static float[] Normalize(float[] v)
    {
        double s = 0;
        foreach (var x in v) s += (double)x * x;
        var n = (float)Math.Sqrt(s);
        return v.Select(x => x / n).ToArray();
    }

    private static float[] Add(float[] a, float[] b) => a.Select((x, i) => x + b[i]).ToArray();
    private static float[] Scale(float[] a, float k) => a.Select(x => x * k).ToArray();
}

/// <summary>
/// Oraculo por fuerza bruta. Misma semantica que el adaptador de Rag.Context.Engine:
/// coseno para denso/resumen, producto punto para sparse, filtros de BuildFilter, orden
/// RankingOrder.Descending (score desc, id ordinal asc) y prefijo completo de empates.
/// </summary>
public static class Oracle
{
    public static bool Matches(Chunk c, EvalFilter f)
    {
        if (f.Tenant is not null && c.Tenant != f.Tenant) return false;
        if (f.Module is not null && !MatchesModuleBoundary(c.Namespace, c.RelativePath, f.Module)) return false;
        if (f.Language is not null && !c.Language.Contains(f.Language, StringComparison.Ordinal)) return false;
        if (f.DefinedAny is not null && !c.DefinedSymbols.Intersect(f.DefinedAny).Any()) return false;
        if (f.Pairs is not null && !f.Pairs.Any(p => c.ClassName == p.Class && c.DefinedSymbols.Contains(p.Method))) return false;
        if (f.Ids is not null && !f.Ids.Contains(c.Id)) return false;
        if (f.ResumenPendingOnly && !c.ResumenPending) return false;
        return true;
    }

    /// <summary>
    /// Contrato de ISemanticRetriever para el filtro de modulo: copia de
    /// QdrantSemanticRetriever.MatchesModuleBoundary (raiz exacta o descendiente por '.' o '/').
    /// El Match.Text de Qdrant es solo prefiltro de candidatos; esto es lo que el puerto promete.
    /// </summary>
    public static bool MatchesModuleBoundary(string? ns, string relativePath, string module) =>
        Boundary(ns, module, '.') || Boundary(relativePath, module, '/');

    private static bool Boundary(string? value, string module, char sep) =>
        value is not null && value.StartsWith(module, StringComparison.Ordinal) &&
        (value.Length == module.Length || value[module.Length] == sep);

    public static List<Hit> Rank(IEnumerable<Chunk> chunks, Branch branch, Query q, EvalFilter f, float? threshold = null)
    {
        var hits = new List<Hit>();
        foreach (var c in chunks)
        {
            if (!Matches(c, f)) continue;
            double s;
            switch (branch)
            {
                case Branch.Dense: s = Dot(c.Dense, q.Dense); break;
                case Branch.Resumen:
                    if (c.Summary is null) continue;
                    s = Dot(c.Summary, q.Dense);
                    break;
                default:
                    s = SparseDot(c.SparseIdx, c.SparseVal, q.SparseIdx, q.SparseVal);
                    if (s <= 0) continue; // sin terminos en comun no es candidato en ningun motor
                    break;
            }
            if (threshold is { } t && s < t) continue;
            hits.Add(new Hit(c.Key, (float)s));
        }
        return RankingOrder(hits).ToList();
    }

    public static IEnumerable<Hit> RankingOrder(IEnumerable<Hit> hits) =>
        hits.OrderByDescending(h => h.Score).ThenBy(h => h.Id, StringComparer.Ordinal);

    public static double Dot(float[] a, float[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += (double)a[i] * b[i];
        return s;
    }

    public static double SparseDot(uint[] ai, float[] av, uint[] bi, float[] bv)
    {
        double s = 0;
        int i = 0, j = 0;
        while (i < ai.Length && j < bi.Length)
        {
            if (ai[i] == bi[j]) { s += (double)av[i] * bv[j]; i++; j++; }
            else if (ai[i] < bi[j]) i++;
            else j++;
        }
        return s;
    }

    /// <summary>Copia de DeterministicVectorQuery.CompletePrefixAsync (Rag.Context.Engine) sobre Hit.</summary>
    public const ulong MaxCandidates = 32768;

    public static async Task<(IReadOnlyList<Hit> Hits, int Queries, ulong LastRequested)> CompletePrefixAsync(
        ulong limit, Func<ulong, Task<IReadOnlyList<Hit>>> query)
    {
        if (limit == 0 || limit >= MaxCandidates) throw new ArgumentOutOfRangeException(nameof(limit));
        var requested = limit + 1;
        var queries = 0;
        while (true)
        {
            queries++;
            var points = await query(requested);
            if (points.Any(p => !float.IsFinite(p.Score)))
                throw new InvalidOperationException("Non-finite vector ranking score.");
            var ordered = RankingOrder(points).ToArray();
            if (ordered.Length < (long)requested || ordered[(int)limit - 1].Score != ordered[^1].Score)
                return (ordered.Take((int)limit).ToArray(), queries, requested);
            if (requested == MaxCandidates)
                throw new InvalidOperationException($"Unresolved ranking tie: candidate limit {MaxCandidates} reached.");
            requested = Math.Min(requested * 2, MaxCandidates);
        }
    }

    /// <summary>
    /// Compara la lista de un motor con la del oraculo. "Exacta": misma secuencia de ids.
    /// "Equivalente": mismas posiciones con |delta score| &lt;= tol y todos los ids por encima del corte
    /// del oraculo (cubre diferencias de redondeo f32 sin ocultar un fallo de recall).
    /// </summary>
    public static (bool Exact, bool Equivalent, string? Detail) Compare(IReadOnlyList<Hit> engine, IReadOnlyList<Hit> oracleFull, int limit, double tol = 1e-4)
    {
        var expected = oracleFull.Take(limit).ToList();
        bool exact = engine.Count == expected.Count && engine.Select(h => h.Id).SequenceEqual(expected.Select(h => h.Id));
        if (exact) return (true, true, null);
        if (engine.Count != expected.Count)
            return (false, false, $"longitud {engine.Count} vs oraculo {expected.Count}");
        var oracleScore = oracleFull.ToDictionary(h => h.Id, h => h.Score);
        var cutoff = expected[^1].Score;
        for (int i = 0; i < engine.Count; i++)
        {
            if (Math.Abs(engine[i].Score - expected[i].Score) > tol)
                return (false, false, $"pos {i}: score {engine[i].Score} vs oraculo {expected[i].Score}");
            if (!oracleScore.TryGetValue(engine[i].Id, out var os) || os < cutoff - tol)
                return (false, false, $"pos {i}: id {engine[i].Id} fuera del top del oraculo");
        }
        return (false, true, "difiere solo en casi-empates dentro de la tolerancia");
    }
}
