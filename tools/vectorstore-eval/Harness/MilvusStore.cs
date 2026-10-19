using System.Text.Json;
using Milvus.Client;

namespace Vse.Eval;

/// <summary>
/// Misma superficie que QdrantStore sobre Milvus.Client 2.6.0. Donde Milvus/el cliente no expresan
/// lo mismo que Qdrant se usa el sustituto mas cercano y queda anotado en <see cref="Gaps"/>:
/// - nombres con guion ("sparse-code", "dense-resumen") no son validos -> sparse_code, dense_resumen;
/// - payload ausente (tenant/namespace sin valor) -> cadena vacia;
/// - dense-resumen opcional por punto -> vector de relleno + has_resumen=false filtrado en la busqueda;
/// - UpdateVectors/SetPayload parciales -> leer fila completa, modificar y upsert completo.
/// </summary>
public sealed class MilvusStore : IStore
{
    public const string DenseName = "dense", SparseName = "sparse_code", SummaryName = "dense_resumen";
    private readonly MilvusClient _c;
    private readonly int _dim = Dataset.Dim;
    private readonly HashSet<string> _hasSummary = new();
    public List<string> Gaps { get; } = new();
    public string ManifestStrategy { get; private set; } = "(sin escribir)";

    public MilvusStore(string host, int port) => _c = new MilvusClient(host, port);
    public MilvusClient Client => _c;
    public string Name => "milvus";

    private static readonly string[] AllFields =
    {
        "id", DenseName, SparseName, "has_resumen", "resumen_pending", "is_manifest", "manifest", "tenant", "namespace",
        "class_name", "method_name", "relative_path", "file_path", "language", "defined_symbols", "consumed_symbols", "content", "extra",
    };

    public async Task RecreateAsync(string coll, int dim, bool summary)
    {
        if (await _c.HasCollectionAsync(coll)) await _c.GetCollection(coll).DropAsync();
        var schema = new CollectionSchema { Name = coll, EnableDynamicFields = false };
        schema.Fields.Add(FieldSchema.CreateVarchar("id", 36, isPrimaryKey: true));
        schema.Fields.Add(FieldSchema.CreateFloatVector(DenseName, dim));
        schema.Fields.Add(FieldSchema.CreateSparseFloatVector(SparseName));
        if (summary) schema.Fields.Add(FieldSchema.CreateFloatVector(SummaryName, dim));
        schema.Fields.Add(FieldSchema.Create<bool>("has_resumen"));
        schema.Fields.Add(FieldSchema.Create<bool>("resumen_pending"));
        schema.Fields.Add(FieldSchema.Create<bool>("is_manifest"));
        schema.Fields.Add(FieldSchema.CreateVarchar("manifest", 65535));
        schema.Fields.Add(FieldSchema.CreateVarchar("tenant", 128));
        schema.Fields.Add(FieldSchema.CreateVarchar("namespace", 512));
        schema.Fields.Add(FieldSchema.CreateVarchar("class_name", 256));
        schema.Fields.Add(FieldSchema.CreateVarchar("method_name", 256));
        schema.Fields.Add(FieldSchema.CreateVarchar("relative_path", 1024));
        schema.Fields.Add(FieldSchema.CreateVarchar("file_path", 2048));
        schema.Fields.Add(FieldSchema.CreateVarchar("language", 64));
        schema.Fields.Add(FieldSchema.CreateVarcharArray("defined_symbols", 128, 256));
        schema.Fields.Add(FieldSchema.CreateVarcharArray("consumed_symbols", 128, 256));
        schema.Fields.Add(FieldSchema.CreateVarchar("content", 65535));
        schema.Fields.Add(FieldSchema.CreateVarchar("extra", 4096));
        var col = await _c.CreateCollectionAsync(coll, schema, ConsistencyLevel.Strong);
        await col.CreateIndexAsync(DenseName, IndexType.Flat, SimilarityMetricType.Cosine);
        if (summary) await col.CreateIndexAsync(SummaryName, IndexType.Flat, SimilarityMetricType.Cosine);
        await col.CreateIndexAsync(SparseName, IndexType.SparseInvertedIndex, SimilarityMetricType.Ip);
        await col.CreateIndexAsync("defined_symbols", IndexType.Inverted);
        await col.CreateIndexAsync("tenant", IndexType.Inverted);
        await col.LoadAsync();
        await col.WaitForCollectionLoadAsync(timeout: TimeSpan.FromMinutes(2));
        if (summary) _hasSummary.Add(coll); else _hasSummary.Remove(coll);
    }

    private sealed record Row(Chunk Chunk, bool IsManifest, string Manifest, float[]? DenseOverride = null, float[]? SummaryPlaceholder = null);

    private float[] Placeholder()
    {
        var v = new float[_dim];
        v[0] = 1f;
        return v;
    }

    private async Task WriteRowsAsync(string coll, IReadOnlyList<Row> rows)
    {
        var col = _c.GetCollection(coll);
        var data = new List<FieldData>
        {
            FieldData.CreateVarChar("id", rows.Select(r => r.Chunk.Key).ToList()),
            FieldData.CreateFloatVector(DenseName, rows.Select(r => (ReadOnlyMemory<float>)(r.DenseOverride ?? r.Chunk.Dense)).ToList()),
            FieldData.CreateSparseFloatVector(SparseName, rows.Select(r => new MilvusSparseVector<float>(
                r.Chunk.SparseIdx.Select(i => checked((int)i)).ToArray(), r.Chunk.SparseVal)).ToList()),
            FieldData.Create("has_resumen", rows.Select(r => r.Chunk.Summary is not null).ToList()),
            FieldData.Create("resumen_pending", rows.Select(r => r.Chunk.ResumenPending).ToList()),
            FieldData.Create("is_manifest", rows.Select(r => r.IsManifest).ToList()),
            FieldData.CreateVarChar("manifest", rows.Select(r => r.Manifest).ToList()),
            FieldData.CreateVarChar("tenant", rows.Select(r => r.Chunk.Tenant ?? "").ToList()),
            FieldData.CreateVarChar("namespace", rows.Select(r => r.Chunk.Namespace ?? "").ToList()),
            FieldData.CreateVarChar("class_name", rows.Select(r => r.Chunk.ClassName ?? "").ToList()),
            FieldData.CreateVarChar("method_name", rows.Select(r => r.Chunk.MethodName ?? "").ToList()),
            FieldData.CreateVarChar("relative_path", rows.Select(r => r.Chunk.RelativePath).ToList()),
            FieldData.CreateVarChar("file_path", rows.Select(r => r.Chunk.FilePath).ToList()),
            FieldData.CreateVarChar("language", rows.Select(r => r.Chunk.Language).ToList()),
            FieldData.CreateArray("defined_symbols", rows.Select(r => (IReadOnlyList<string>)r.Chunk.DefinedSymbols).ToList()),
            FieldData.CreateArray("consumed_symbols", rows.Select(r => (IReadOnlyList<string>)r.Chunk.ConsumedSymbols).ToList()),
            FieldData.CreateVarChar("content", rows.Select(r => r.Chunk.Content).ToList()),
            FieldData.CreateVarChar("extra", rows.Select(r => JsonSerializer.Serialize(new
            {
                r.Chunk.StartLine, r.Chunk.EndLine, r.Chunk.ChunkType, r.Chunk.ContentHash, r.Chunk.LastModified, r.Chunk.RepositoryName,
            })).ToList()),
        };
        if (_hasSummary.Contains(coll))
            data.Add(FieldData.CreateFloatVector(SummaryName,
                rows.Select(r => (ReadOnlyMemory<float>)(r.Chunk.Summary ?? r.SummaryPlaceholder ?? Placeholder())).ToList()));
        await col.UpsertAsync(data);
    }

    public Task UpsertAsync(string coll, IReadOnlyList<Chunk> batch, bool wait = true) =>
        WriteRowsAsync(coll, batch.Select(c => new Row(c, false, "")).ToList());

    /// <summary>Numero de flushes rechazados por el limitador por defecto de Milvus (quotaAndLimits flushRate).</summary>
    public int FlushRateLimited { get; private set; }

    /// <summary>Tipo .NET con el que el cliente entrega count(*): cambia segun la version del servidor.</summary>
    public string? CountFieldType { get; private set; }

    public async Task FlushAsync(string coll)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { await _c.GetCollection(coll).FlushAsync(); return; }
            catch (MilvusException ex) when (ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) && attempt < 3)
            {
                FlushRateLimited++;
                await Task.Delay(TimeSpan.FromSeconds(11));
            }
        }
    }

    private static readonly Guid ManifestId = QdrantStore.ManifestPointId;

    public async Task UpsertManifestAsync(string coll, string json, int dim)
    {
        var shell = new Chunk(ManifestId, new float[dim], Array.Empty<uint>(), Array.Empty<float>(), null, false,
            "", "", "", "", null, null, null, 0, 0, "", "", "", "", Array.Empty<string>(), Array.Empty<string>(), null);
        try
        {
            // Igual que Qdrant: denso de ceros y sin sparse.
            await WriteRowsAsync(coll, new[] { new Row(shell, true, json) });
            ManifestStrategy = "denso de ceros y sparse vacio (igual que Qdrant)";
        }
        catch (Exception ex)
        {
            Gaps.Add($"manifiesto: denso de ceros/sparse vacio rechazado ({Short(ex)}); se usa relleno");
            var placeholder = Placeholder();
            await WriteRowsAsync(coll, new[] { new Row(shell with { SparseIdx = new uint[] { 0 }, SparseVal = new[] { 1f } }, true, json, placeholder) });
            ManifestStrategy = "relleno: denso e0 y sparse {0:1}, excluido por is_manifest";
        }
    }

    public async Task<string?> GetManifestAsync(string coll)
    {
        var p = new QueryParameters { ConsistencyLevel = ConsistencyLevel.Strong };
        p.OutputFields.Add("manifest");
        var r = await _c.GetCollection(coll).QueryAsync($"id == \"{ManifestId:D}\" and is_manifest == true", p);
        var f = r.FirstOrDefault(x => x.FieldName == "manifest");
        return f is null || f.RowCount == 0 ? null : (string)f.Val(0)!;
    }

    public static string BuildExpr(EvalFilter f, Branch? branch = null)
    {
        var parts = new List<string> { "is_manifest == false" };
        if (branch == Branch.Resumen) parts.Add("has_resumen == true");
        if (f.Tenant is not null) parts.Add($"tenant == {Q(f.Tenant)}");
        if (f.Language is not null) parts.Add($"language like {Q("%" + f.Language + "%")}");
        // Frontera exacta de MatchesModuleBoundary expresada en el servidor (un like "%m%" dejaria pasar
        // Reyma.VentasExtra y el post-filtro recortaria el top-K). Asume modulos sin '%' ni '_'.
        if (f.Module is not null)
            parts.Add($"(namespace == {Q(f.Module)} or namespace like {Q(f.Module + ".%")} or relative_path == {Q(f.Module)} or relative_path like {Q(f.Module + "/%")})");
        if (f.DefinedAny is not null) parts.Add($"array_contains_any(defined_symbols, [{string.Join(", ", f.DefinedAny.Select(Q))}])");
        if (f.Pairs is not null)
            parts.Add("(" + string.Join(" or ", f.Pairs.Select(p => $"(class_name == {Q(p.Class)} and array_contains(defined_symbols, {Q(p.Method)}))")) + ")");
        if (f.Ids is not null) parts.Add($"id in [{string.Join(", ", f.Ids.Select(i => Q(i.ToString("D"))))}]");
        if (f.ResumenPendingOnly) parts.Add("resumen_pending == true");
        return string.Join(" and ", parts);
    }

    private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public async Task<IReadOnlyList<Hit>> SearchAsync(string coll, Branch branch, Query q, EvalFilter f, ulong limit, float? threshold = null)
    {
        var col = _c.GetCollection(coll);
        var p = new SearchParameters { Expression = BuildExpr(f, branch), ConsistencyLevel = ConsistencyLevel.Strong };
        if (threshold is { } t) p.Radius = t;
        SearchResults r;
        if (branch == Branch.Sparse)
            r = await col.SearchAsync(SparseName,
                new List<MilvusSparseVector<float>> { new(q.SparseIdx.Select(i => (int)i).ToArray(), q.SparseVal) },
                SimilarityMetricType.Ip, (int)limit, p);
        else
            r = await col.SearchAsync(branch == Branch.Dense ? DenseName : SummaryName,
                new List<ReadOnlyMemory<float>> { q.Dense }, SimilarityMetricType.Cosine, (int)limit, p);
        var ids = r.Ids.StringIds ?? Array.Empty<string>();
        return ids.Select((id, i) => new Hit(id, r.Scores[i])).ToList();
    }

    private async Task<List<Row>> ReadRowsAsync(string coll, IReadOnlyList<Guid> ids)
    {
        var p = new QueryParameters { ConsistencyLevel = ConsistencyLevel.Strong };
        foreach (var f in AllFields) p.OutputFields.Add(f);
        if (_hasSummary.Contains(coll)) p.OutputFields.Add(SummaryName);
        var r = await _c.GetCollection(coll).QueryAsync($"id in [{string.Join(", ", ids.Select(i => Q(i.ToString("D"))))}]", p);
        FieldData F(string n) => r.First(x => x.FieldName == n);
        var n = (int)F("id").RowCount;
        var rows = new List<Row>(n);
        for (int i = 0; i < n; i++)
        {
            string S(string k) => (string)F(k).Val(i)!;
            bool B(string k) => (bool)F(k).Val(i)!;
            string[] A(string k) => ((ArrayFieldData<string>)F(k)).Data[i].ToArray();
            string? N(string k) => S(k) is { Length: > 0 } s ? s : null;
            var dense = ((FloatVectorFieldData)F(DenseName)).Data[i].ToArray();
            var sp = ((SparseFloatVectorFieldData)F(SparseName)).Data[i];
            var hasSummary = B("has_resumen");
            float[]? summary = hasSummary && _hasSummary.Contains(coll) ? ((FloatVectorFieldData)F(SummaryName)).Data[i].ToArray() : null;
            var extra = JsonDocument.Parse(S("extra")).RootElement;
            var chunk = new Chunk(
                Guid.Parse(S("id")), dense, sp.Indices.ToArray().Select(x => (uint)x).ToArray(), sp.Values.ToArray(), summary,
                B("resumen_pending"), S("content"), S("file_path"), S("relative_path"), S("language"), N("namespace"),
                N("class_name"), N("method_name"), extra.GetProperty("StartLine").GetInt32(), extra.GetProperty("EndLine").GetInt32(),
                extra.GetProperty("ChunkType").GetString()!, extra.GetProperty("ContentHash").GetString()!,
                extra.GetProperty("LastModified").GetString()!, extra.GetProperty("RepositoryName").GetString()!,
                A("defined_symbols"), A("consumed_symbols"), N("tenant"));
            rows.Add(new Row(chunk, B("is_manifest"), S("manifest")));
        }
        return rows;
    }

    public async Task UpdateSummaryVectorAsync(string coll, Guid id, float[] v)
    {
        // Sin UpdateVectors parcial en el cliente: leer fila completa y reescribirla.
        var rows = await ReadRowsAsync(coll, new[] { id });
        await WriteRowsAsync(coll, rows.Select(r => r with { Chunk = r.Chunk with { Summary = v } }).ToList());
    }

    public async Task MarkResumenCompleteAsync(string coll, IReadOnlyList<Guid> ids)
    {
        var rows = await ReadRowsAsync(coll, ids);
        await WriteRowsAsync(coll, rows.Select(r => r with { Chunk = r.Chunk with { ResumenPending = false } }).ToList());
    }

    public async Task<long> CountAsync(string coll, EvalFilter f)
    {
        var p = new QueryParameters { ConsistencyLevel = ConsistencyLevel.Strong };
        p.OutputFields.Add("count(*)");
        var r = await _c.GetCollection(coll).QueryAsync(BuildExpr(f), p);
        var cell = r.First(x => x.FieldName == "count(*)");
        CountFieldType = cell.GetType().ToString();
        return Convert.ToInt64(cell.Val(0));
    }

    public async Task<List<(string Id, string? FilePath)>> ScrollAllAsync(string coll)
    {
        var p = new QueryParameters { ConsistencyLevel = ConsistencyLevel.Strong };
        p.OutputFields.Add("id");
        p.OutputFields.Add("file_path");
        var all = new List<(string, string?)>();
        await foreach (var batch in _c.GetCollection(coll).QueryWithIteratorAsync("id != \"\"", 512, p))
        {
            var ids = batch.First(x => x.FieldName == "id");
            var fps = batch.First(x => x.FieldName == "file_path");
            for (int i = 0; i < ids.RowCount; i++)
                all.Add(((string)ids.Val(i)!, (string?)fps.Val(i) is { Length: > 0 } s ? s : null));
        }
        return all;
    }

    public async Task<Dictionary<string, (bool Pending, float[]? Summary)>> RetrieveAsync(string coll, IReadOnlyList<Guid> ids) =>
        (await ReadRowsAsync(coll, ids)).ToDictionary(r => r.Chunk.Key, r => (r.Chunk.ResumenPending, r.Chunk.Summary));

    public async Task DeleteAsync(string coll, IReadOnlyList<Guid> ids) =>
        await _c.GetCollection(coll).DeleteAsync($"id in [{string.Join(", ", ids.Select(i => Q(i.ToString("D"))))}]");

    public async Task<SchemaInfo> DescribeAsync(string coll)
    {
        var col = _c.GetCollection(coll);
        var d = await col.DescribeAsync();
        var dense = d.Schema.Fields.Where(f => f.DataType == MilvusDataType.FloatVector).ToDictionary(f => f.Name, f => (ulong)(f.Dimension ?? 0));
        var sparse = d.Schema.Fields.Where(f => f.DataType == MilvusDataType.SparseFloatVector).Select(f => f.Name).ToArray();
        string[] indexed;
        try
        {
            var idx = await col.DescribeIndexAsync(DenseName);
            indexed = new[] { "(DescribeIndexAsync por campo)" };
            indexed = (await Task.WhenAll(new[] { "defined_symbols", "tenant" }.Select(async fname =>
            {
                try { var x = await col.DescribeIndexAsync(fname); return x.Count > 0 ? fname : null; } catch { return null; }
            }))).Where(x => x is not null).Select(x => x!).ToArray();
        }
        catch { indexed = Array.Empty<string>(); }
        var count = await CountAsync(coll, new EvalFilter("all"));
        return new SchemaInfo(dense, sparse, indexed, count);
    }

    public async Task<IReadOnlyList<string>> ListCollectionsAsync() => (await _c.ListCollectionsAsync()).Select(c => c.Name).ToList();
    public async Task DropAsync(string coll) { if (await _c.HasCollectionAsync(coll)) await _c.GetCollection(coll).DropAsync(); }

    /// <summary>Acceso tipado a una celda: FieldData.GetRowValue es interno en Milvus.Client 2.6.0.</summary>

    public static object? CellValue(FieldData f, int i) => f switch

    {

        FieldData<string> s => s.Data[i],

        FieldData<bool> b => b.Data[i],

        FieldData<long> l => l.Data[i],

        FieldData<int> n => n.Data[i],

        // Cualquier otro FieldData<T>: Milvus 3.0.3 devuelve count(*) con un T distinto que 2.6.25.
        _ when f.GetType().GetProperty("Data")?.GetValue(f) is System.Collections.IList list => list[i],
        _ => throw new NotSupportedException($"celda de tipo {f.GetType()} en {f.FieldName}"),

    };


    public static string Short(Exception ex)
    {
        var m = ex.Message.Replace('\n', ' ');
        return m.Length > 300 ? m[..300] : m;
    }
}


public static class FieldDataExtensions
{
    public static object? Val(this FieldData f, int i) => MilvusStore.CellValue(f, i);
}

