// 1.2-comparar-qdrant-vs-milvus: harness de paridad funcional (criterio 1) y migracion (criterio 2).
// Uso: dotnet run -c Release -- <salida.json>
// Requiere tools/vectorstore-eval/docker-compose.yml arriba (Qdrant 127.0.0.1:16334, Milvus 127.0.0.1:19530).
using System.Diagnostics;
using System.Text.Json;
using Milvus.Client;
using Vse.Eval;

var outPath = args.Length > 0 ? args[0] : "resultado.json";
const int N = 3000;
const string Main = "vse_eval_main";
var results = new List<TestResult>();
var timings = new Dictionary<string, double>();
var seed = args.Length > 1 ? int.Parse(args[1]) : 12345;
var (chunks, queries, tieDense) = Dataset.Generate(N, seed);
var tieChunk = chunks.First(c => ReferenceEquals(c.Dense, tieDense));
var tieQuery = new Query(-1, tieDense, tieChunk.SparseIdx, tieChunk.SparseVal);
var tieIds = chunks.Where(c => ReferenceEquals(c.Dense, tieDense)).Select(c => c.Key).ToHashSet();

var qdrant = new QdrantStore("127.0.0.1", 16334);
var milvus = new MilvusStore("127.0.0.1", 19530);
var versions = new Dictionary<string, string>
{
    ["qdrant_server"] = (await qdrant.Client.HealthAsync()).Version,
    ["milvus_server"] = await milvus.Client.GetVersionAsync(),
    ["qdrant_client"] = typeof(Qdrant.Client.QdrantClient).Assembly.GetName().Version!.ToString(),
    ["milvus_client"] = typeof(MilvusClient).Assembly.GetName().Version!.ToString(),
};
Console.WriteLine(string.Join(" | ", versions.Select(kv => $"{kv.Key}={kv.Value}")));
Console.WriteLine($"dataset: {chunks.Count} puntos, grupo de empate {tieIds.Count}, con resumen {chunks.Count(c => c.Summary is not null)}");

var pairs = chunks.Where(c => c.ClassName is not null && c.DefinedSymbols.Length > 0).Take(5)
    .Select(c => (c.ClassName!, c.DefinedSymbols[0])).ToList();
var rndIds = new Random(7);
var someIds = chunks.OrderBy(_ => rndIds.Next()).Take(200).Select(c => c.Id).ToList();
var filters = new List<EvalFilter>
{
    new("sin-filtro"),
    new("tenant-a", Tenant: "tenant-a"),
    new("modulo-Reyma.Ventas", Module: "Reyma.Ventas"),
    new("lenguaje-Markdown", Language: "Markdown"),
    new("defined_symbols-any", DefinedAny: Enumerable.Range(1, 20).Select(i => $"Sym{i:000}").ToList()),
    new("salto-cualificado", Pairs: pairs),
    new("ids-200", Ids: someIds),
};

foreach (IStore store in new IStore[] { qdrant, milvus })
{
    var e = store.Name;
    var oracle = chunks.ToList();
    void Add(string crit, string test, bool pass, string detail)
    {
        results.Add(new TestResult(e, crit, test, pass, detail));
        Console.WriteLine($"[{e}] {(pass ? "OK  " : "FAIL")} {test}: {detail}");
    }
    async Task Guard(string test, Func<Task> body)
    {
        try { await body(); }
        catch (Exception ex) { Add("1", test, false, "excepcion: " + MilvusStore.Short(ex)); }
    }

    // A. Esquema + carga en dos tandas (en Milvus la segunda queda en segmento "growing" sin flush).
    var sw = Stopwatch.StartNew();
    await Guard("crear-coleccion-hibrida", async () =>
    {
        await store.RecreateAsync(Main, Dataset.Dim, summary: true);
        Add("1", "crear-coleccion-hibrida", true, "dense + sparse + dense-resumen opcional + indice keyword defined_symbols");
    });
    timings[$"{e}.crear_coleccion_s"] = sw.Elapsed.TotalSeconds;
    sw.Restart();
    int cut = (int)(N * 0.6);
    foreach (var b in chunks.Take(cut).Chunk(256)) await store.UpsertAsync(Main, b);
    await store.FlushAsync(Main);
    foreach (var b in chunks.Skip(cut).Chunk(256)) await store.UpsertAsync(Main, b);
    timings[$"{e}.carga_{N}_puntos_s"] = sw.Elapsed.TotalSeconds;

    await Guard("manifiesto-ida-y-vuelta", async () =>
    {
        var json = "{\"ModelName\":\"sintetico\",\"EmbeddingDimension\":384,\"Tenants\":[\"tenant-a\"]}";
        await store.UpsertManifestAsync(Main, json, Dataset.Dim);
        var back = await store.GetManifestAsync(Main);
        var extra = store is MilvusStore ms ? $"; estrategia: {ms.ManifestStrategy}" : "; denso de ceros sin sparse";
        Add("1", "manifiesto-ida-y-vuelta", back == json, (back == json ? "identico" : $"difiere: {back}") + extra);
    });

    async Task Ties(string phase)
    {
        await Guard($"empates-prefijo-completo-{phase}", async () =>
        {
            foreach (var br in new[] { Branch.Dense, Branch.Sparse })
            {
                var (hits, nq, last) = await Oracle.CompletePrefixAsync(10, l => store.SearchAsync(Main, br, tieQuery, filters[0], l));
                var expected = Oracle.Rank(oracle, br, tieQuery, filters[0]);
                var cmp = Oracle.Compare(hits, expected, 10);
                var raw = await store.SearchAsync(Main, br, tieQuery, filters[0], 60);
                var tieScores = raw.Where(h => tieIds.Contains(h.Id)).Select(h => h.Score).Distinct().ToList();
                bool bitwise = tieScores.Count == 1;
                Add("1", $"empates-prefijo-completo-{phase}-{br}", cmp.Exact && bitwise,
                    $"{nq} consultas (ultimo limit {last}); top10 {(cmp.Exact ? "identico al oraculo" : cmp.Detail)}; " +
                    $"scores del grupo de {tieIds.Count}: {tieScores.Count} valor(es) distinto(s) {(bitwise ? "(empate bit a bit)" : string.Join(",", tieScores.Select(s => s.ToString("R"))))}");
            }
        });
    }

    async Task Parity(string phase, int nQueries)
    {
        await Guard($"paridad-{phase}", async () =>
        {
            foreach (var br in new[] { Branch.Dense, Branch.Sparse, Branch.Resumen })
            {
                int exact = 0, equiv = 0, fail = 0;
                var fails = new List<string>();
                foreach (var f in filters)
                    foreach (var q in queries.Take(nQueries))
                    {
                        var expected = Oracle.Rank(oracle, br, q, f);
                        var (hits, _, _) = await Oracle.CompletePrefixAsync(40, l => store.SearchAsync(Main, br, q, f, l));
                        var cmp = Oracle.Compare(hits, expected, 40);
                        if (cmp.Exact) exact++; else if (cmp.Equivalent) equiv++; else { fail++; if (fails.Count < 3) fails.Add($"{f.Label}/q{q.Index}: {cmp.Detail}"); }
                    }
                Add("1", $"paridad-{phase}-{br}", fail == 0,
                    $"{filters.Count} filtros x {nQueries} consultas, top40 exacto: {exact} identicas, {equiv} equivalentes, {fail} fallos" +
                    (fails.Count > 0 ? " -> " + string.Join(" | ", fails) : ""));
            }
        });
    }

    await Ties("con-segmento-sin-flush");
    sw.Restart();
    await Parity("con-segmento-sin-flush", queries.Count);
    timings[$"{e}.paridad_{filters.Count * queries.Count * 3}_consultas_s"] = sw.Elapsed.TotalSeconds;

    await Guard("umbral-de-score", async () =>
    {
        var q = queries[0];
        var full = Oracle.Rank(oracle, Branch.Dense, q, filters[0]);
        var mid = (full[49].Score + full[50].Score) / 2f;
        var hits = await store.SearchAsync(Main, Branch.Dense, q, filters[0], 500, mid);
        var cmp = Oracle.Compare(Oracle.RankingOrder(hits).ToList(), full, 50);
        Add("1", "umbral-de-score-intermedio", cmp.Exact && hits.Count == 50, $"umbral {mid:R}: {hits.Count} resultados (oraculo 50); {(cmp.Exact ? "identicos" : cmp.Detail)}");
        // Borde: umbral igual al score que el propio motor da al puesto 50 -> inclusivo (>=) o estricto (>).
        var own = Oracle.RankingOrder(await store.SearchAsync(Main, Branch.Dense, q, filters[0], 60)).ToList();
        var edge = own[49].Score;
        var atEdge = await store.SearchAsync(Main, Branch.Dense, q, filters[0], 500, edge);
        bool inclusive = atEdge.Any(h => h.Id == own[49].Id);
        // Informativo: lo que se compara es que ambos motores tengan la MISMA semantica en el borde.
        Add("1", "umbral-de-score-en-el-borde", true,
            $"umbral = score propio del puesto 50 ({edge:R}): {atEdge.Count} resultados -> {(inclusive ? "inclusivo (>=)" : "estricto (>): el puesto con score igual al umbral queda fuera")}");
    });

    await store.FlushAsync(Main);
    await Ties("tras-flush");
    await Parity("tras-flush", 10);

    // G. Mutaciones de IVectorStoreWriter.
    var x = oracle.First(c => c.Tenant == "tenant-a" && c.Summary is not null && !tieIds.Contains(c.Key));
    await Guard("upsert-reemplazo-completo", async () =>
    {
        var before = await store.CountAsync(Main, filters[1]);
        var replaced = x with { Tenant = null, Summary = null, ResumenPending = true };
        await store.UpsertAsync(Main, new[] { replaced });
        oracle[oracle.FindIndex(c => c.Id == x.Id)] = replaced;
        var after = await store.CountAsync(Main, filters[1]);
        var resumenHit = await store.SearchAsync(Main, Branch.Resumen, new Query(-2, x.Summary!, x.SparseIdx, x.SparseVal), new EvalFilter("x", Ids: new[] { x.Id }), 5);
        var st = (await store.RetrieveAsync(Main, new[] { x.Id }))[x.Key];
        bool ok = after == before - 1 && resumenHit.Count == 0 && st.Pending && st.Summary is null;
        Add("1", "upsert-reemplazo-completo", ok, $"tenant-a {before}->{after}; busqueda resumen del punto: {resumenHit.Count} hits; retrieve pending={st.Pending} resumen={(st.Summary is null ? "ausente" : "presente")}");
    });
    await Guard("update-vector-resumen", async () =>
    {
        var v = Dataset.Normalize(Dataset.Gaussian(new Random(99), Dataset.Dim));
        await store.UpdateSummaryVectorAsync(Main, x.Id, v);
        var idx = oracle.FindIndex(c => c.Id == x.Id);
        oracle[idx] = oracle[idx] with { Summary = v };
        var hits = await store.SearchAsync(Main, Branch.Resumen, new Query(-3, v, x.SparseIdx, x.SparseVal), filters[0], 1);
        var st = (await store.RetrieveAsync(Main, new[] { x.Id }))[x.Key];
        var tenantKept = await store.CountAsync(Main, new EvalFilter("x", Ids: new[] { x.Id }));
        bool ok = hits.Count == 1 && hits[0].Id == x.Key && hits[0].Score > 0.999f && st.Summary is not null && tenantKept == 1;
        Add("1", "update-vector-resumen", ok, $"top1 resumen={(hits.Count > 0 ? hits[0].Id == x.Key : false)} score={(hits.Count > 0 ? hits[0].Score : 0):R}; resto del punto intacto={tenantKept == 1}");
    });
    await Guard("marcar-resumen-completo", async () =>
    {
        var pend = new EvalFilter("pend", ResumenPendingOnly: true);
        var before = await store.CountAsync(Main, pend);
        var ids = oracle.Where(c => c.ResumenPending && !tieIds.Contains(c.Key)).Take(10).ToList();
        await store.MarkResumenCompleteAsync(Main, ids.Select(c => c.Id).ToList());
        foreach (var c in ids) oracle[oracle.FindIndex(o => o.Id == c.Id)] = c with { ResumenPending = false };
        var after = await store.CountAsync(Main, pend);
        var q0 = ids[0];
        var dense = await store.SearchAsync(Main, Branch.Dense, new Query(-4, q0.Dense, q0.SparseIdx, q0.SparseVal), new EvalFilter("x", Ids: new[] { q0.Id }), 1);
        bool ok = after == before - 10 && dense.Count == 1 && dense[0].Score > 0.999f;
        Add("1", "marcar-resumen-completo", ok, $"pendientes {before}->{after} (esperado -10); denso del punto intacto={dense.Count == 1 && dense[0].Score > 0.999f}");
    });
    await Guard("scroll-completo", async () =>
    {
        var all = await store.ScrollAllAsync(Main);
        var ids = all.Select(a => a.Id).ToList();
        var expected = oracle.Select(c => c.Key).Append(QdrantStore.ManifestPointId.ToString("D")).ToHashSet();
        bool ok = ids.Count == ids.Distinct().Count() && ids.ToHashSet().SetEquals(expected);
        Add("1", "scroll-completo", ok, $"{ids.Count} puntos paginados (esperado {expected.Count}), duplicados={ids.Count - ids.Distinct().Count()}");
    });
    await Guard("retrieve-estado-resumen", async () =>
    {
        var sample = oracle.Where((_, i) => i % 600 == 0).ToList();
        var got = await store.RetrieveAsync(Main, sample.Select(c => c.Id).ToList());
        int bad = sample.Count(c => !got.TryGetValue(c.Key, out var s) || s.Pending != c.ResumenPending || (s.Summary is null) != (c.Summary is null));
        Add("1", "retrieve-estado-resumen", bad == 0, $"{sample.Count} puntos, {bad} con estado distinto al esperado");
    });
    await Guard("borrar-por-id", async () =>
    {
        var all = new EvalFilter("all");
        var before = await store.CountAsync(Main, all);
        var del = oracle.Where(c => !tieIds.Contains(c.Key)).Skip(100).Take(5).ToList();
        await store.DeleteAsync(Main, del.Select(c => c.Id).ToList());
        oracle.RemoveAll(c => del.Any(d => d.Id == c.Id));
        var after = await store.CountAsync(Main, all);
        var left = await store.SearchAsync(Main, Branch.Dense, new Query(-5, del[0].Dense, del[0].SparseIdx, del[0].SparseVal), new EvalFilter("x", Ids: del.Select(d => d.Id).ToList()), 10);
        Add("1", "borrar-por-id", after == before - 5 && left.Count == 0, $"conteo {before}->{after}; busqueda de los borrados: {left.Count} hits");
    });
    await Guard("lectura-tras-escritura", async () =>
    {
        var fresh = chunks[0] with { Id = Guid.NewGuid(), Dense = Dataset.Normalize(Dataset.Gaussian(new Random(5), Dataset.Dim)) };
        await store.UpsertAsync(Main, new[] { fresh }, wait: true);
        oracle.Add(fresh);
        var hits = await store.SearchAsync(Main, Branch.Dense, new Query(-6, fresh.Dense, fresh.SparseIdx, fresh.SparseVal), filters[0], 1);
        Add("1", "lectura-tras-escritura", hits.Count == 1 && hits[0].Id == fresh.Key, $"top1 inmediato = punto recien escrito: {hits.Count == 1 && hits[0].Id == fresh.Key}");
    });
    await Guard("paridad-tras-mutaciones", async () =>
    {
        int fail = 0;
        foreach (var br in new[] { Branch.Dense, Branch.Sparse, Branch.Resumen })
            foreach (var q in queries.Take(10))
            {
                var (hits, _, _) = await Oracle.CompletePrefixAsync(40, l => store.SearchAsync(Main, br, q, filters[1], l));
                if (!Oracle.Compare(hits, Oracle.Rank(oracle, br, q, filters[1]), 40).Equivalent) fail++;
            }
        Add("1", "paridad-tras-mutaciones", fail == 0, $"3 ramas x 10 consultas con filtro tenant-a tras las mutaciones: {fail} fallos");
    });
    await Guard("inspeccion-de-esquema", async () =>
    {
        var s = await store.DescribeAsync(Main);
        var list = await store.ListCollectionsAsync();
        bool ok = list.Contains(Main) && s.Dense.Count == 2 && s.Dense.Values.All(d => d == Dataset.Dim) && s.Sparse.Length == 1 && s.IndexedPayload.Contains("defined_symbols");
        Add("1", "inspeccion-de-esquema", ok, $"densos [{string.Join(", ", s.Dense.Select(kv => $"{kv.Key}:{kv.Value}"))}], sparse [{string.Join(", ", s.Sparse)}], indices payload [{string.Join(", ", s.IndexedPayload)}], puntos {s.Points}");
    });
    foreach (var lim in new ulong[] { 16384, 16385, 32767 })
        await Guard($"limite-{lim}", async () =>
        {
            var hits = await store.SearchAsync(Main, Branch.Dense, queries[0], filters[0], lim);
            Add("1", $"limite-{lim}", true, $"aceptado, {hits.Count} resultados (la coleccion tiene {oracle.Count})");
        });
}

// Sondas de Milvus: lo que el esquema de Rag.Context.Engine pide y Milvus podria no aceptar tal cual.
async Task Probe(string name, Func<Task<string>> body)
{
    try { var d = await body(); results.Add(new TestResult("milvus", "1", "sonda-" + name, true, d)); Console.WriteLine($"[milvus] sonda {name}: {d}"); }
    catch (Exception ex) { results.Add(new TestResult("milvus", "1", "sonda-" + name, false, MilvusStore.Short(ex))); Console.WriteLine($"[milvus] sonda {name}: FALLA {MilvusStore.Short(ex)}"); }
}
await Probe("nombre-con-guion", async () =>
{
    var s = new CollectionSchema { Name = "vse_probe_hyphen" };
    s.Fields.Add(FieldSchema.CreateVarchar("id", 36, isPrimaryKey: true));
    s.Fields.Add(FieldSchema.CreateSparseFloatVector("sparse-code"));
    await milvus.DropAsync("vse_probe_hyphen");
    await milvus.Client.CreateCollectionAsync("vse_probe_hyphen", s);
    await milvus.DropAsync("vse_probe_hyphen");
    return "aceptado 'sparse-code'";
});
await Probe("vector-nullable", async () =>
{
    var s = new CollectionSchema { Name = "vse_probe_nullable" };
    s.Fields.Add(FieldSchema.CreateVarchar("id", 36, isPrimaryKey: true));
    s.Fields.Add(FieldSchema.CreateFloatVector("dense", 4));
    var nv = FieldSchema.Create("dense_resumen", MilvusDataType.FloatVector, nullable: true);
    nv.Dimension = 4;
    s.Fields.Add(nv);
    await milvus.DropAsync("vse_probe_nullable");
    var col = await milvus.Client.CreateCollectionAsync("vse_probe_nullable", s);
    string step = "esquema con vector nullable creado";
    try
    {
        await col.InsertAsync(new List<FieldData>
        {
            FieldData.CreateVarChar("id", new[] { "a" }),
            FieldData.CreateFloatVector("dense", new List<ReadOnlyMemory<float>> { new float[] { 1, 0, 0, 0 } }),
        });
        step += "; insertar una fila SIN dense_resumen: aceptado";
    }
    catch (Exception ex) { step += "; insertar una fila SIN dense_resumen: rechazado (" + MilvusStore.Short(ex) + ")"; }
    await milvus.DropAsync("vse_probe_nullable");
    return step + ". El cliente no tiene forma de enviar un vector null en una fila (FloatVectorFieldData sin mascara de validez).";
});
foreach (var c in new[] { "vse_probe_hyphen", "vse_probe_nullable" }) await milvus.DropAsync(c);

// Criterio 2: migracion medida Qdrant -> Milvus de una coleccion con la forma de Rag.Context.Engine.
const string Src = "vse_eval_src", Dst = "vse_eval_migrated";
var migration = new Dictionary<string, object>();
try
{
    await qdrant.RecreateAsync(Src, Dataset.Dim, summary: true);
    foreach (var b in chunks.Chunk(256)) await qdrant.UpsertAsync(Src, b);
    await qdrant.UpsertManifestAsync(Src, "{\"ModelName\":\"sintetico\"}", Dataset.Dim);
    await milvus.RecreateAsync(Dst, Dataset.Dim, summary: true);
    var sw = Stopwatch.StartNew();
    int moved = 0;
    string? manifest = null;
    await foreach (var (page, m) in qdrant.ExportAsync(Src))
    {
        if (page.Count > 0) await milvus.UpsertAsync(Dst, page);
        moved += page.Count;
        manifest ??= m;
    }
    if (manifest is not null) await milvus.UpsertManifestAsync(Dst, manifest, Dataset.Dim);
    await milvus.FlushAsync(Dst);
    var secs = sw.Elapsed.TotalSeconds;
    var count = await milvus.CountAsync(Dst, new EvalFilter("all"));
    int same = 0, equiv = 0, diff = 0;
    foreach (var br in new[] { Branch.Dense, Branch.Sparse, Branch.Resumen })
        foreach (var f in new[] { filters[0], filters[1] })
            foreach (var q in queries)
            {
                var (a, _, _) = await Oracle.CompletePrefixAsync(40, l => qdrant.SearchAsync(Src, br, q, f, l));
                var (b, _, _) = await Oracle.CompletePrefixAsync(40, l => milvus.SearchAsync(Dst, br, q, f, l));
                var cmp = Oracle.Compare(b, a, 40);
                if (cmp.Exact) same++; else if (cmp.Equivalent) equiv++; else diff++;
            }
    var manifestBack = await milvus.GetManifestAsync(Dst);
    migration = new Dictionary<string, object>
    {
        ["puntos_migrados"] = moved, ["conteo_destino"] = count, ["segundos"] = Math.Round(secs, 2),
        ["puntos_por_segundo"] = Math.Round(moved / secs, 1), ["manifiesto_identico"] = manifestBack == manifest,
        ["busquedas_identicas_origen_destino"] = same, ["busquedas_equivalentes"] = equiv, ["busquedas_distintas"] = diff,
    };
    bool okm = count == moved && moved == N && manifestBack == manifest && diff == 0;
    results.Add(new TestResult("migracion", "2", "migracion-qdrant-a-milvus", okm, JsonSerializer.Serialize(migration)));
    Console.WriteLine($"[migracion] {(okm ? "OK" : "FAIL")} {JsonSerializer.Serialize(migration)}");
}
catch (Exception ex)
{
    results.Add(new TestResult("migracion", "2", "migracion-qdrant-a-milvus", false, MilvusStore.Short(ex)));
    Console.WriteLine($"[migracion] FAIL {MilvusStore.Short(ex)}");
}

var report = new
{
    ficha = "1.2-comparar-qdrant-vs-milvus",
    versiones = versions,
    dataset = new { semilla = seed, puntos = N, dimension = Dataset.Dim, grupo_empate = tieIds.Count, consultas = queries.Count, filtros = filters.Select(f => f.Label) },
    milvus_brechas_de_adaptador = milvus.Gaps,
    milvus_flush_rechazados_por_rate_limit = milvus.FlushRateLimited,
    milvus_tipo_count = milvus.CountFieldType,
    tiempos_s = timings.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2)),
    resultados = results,
    resumen = results.GroupBy(r => r.Engine).ToDictionary(g => g.Key, g => new { ok = g.Count(r => r.Pass), fallos = g.Count(r => !r.Pass) }),
};
File.WriteAllText(outPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"Escrito {outPath}");
foreach (var g in results.GroupBy(r => r.Engine)) Console.WriteLine($"{g.Key}: {g.Count(r => r.Pass)} OK, {g.Count(r => !r.Pass)} fallos");

public sealed record TestResult(string Engine, string Criterion, string Test, bool Pass, string Detail);
