using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Vse.Eval;

/// <summary>Las operaciones que necesitan ISemanticRetriever, IVectorStoreWriter e IVectorStoreAdmin.</summary>
public interface IStore
{
    string Name { get; }
    Task RecreateAsync(string coll, int dim, bool summary);
    Task UpsertAsync(string coll, IReadOnlyList<Chunk> batch, bool wait = true);
    Task FlushAsync(string coll);
    Task UpsertManifestAsync(string coll, string json, int dim);
    Task<string?> GetManifestAsync(string coll);
    Task<IReadOnlyList<Hit>> SearchAsync(string coll, Branch branch, Query q, EvalFilter f, ulong limit, float? threshold = null);
    Task UpdateSummaryVectorAsync(string coll, Guid id, float[] v);
    Task MarkResumenCompleteAsync(string coll, IReadOnlyList<Guid> ids);
    Task<long> CountAsync(string coll, EvalFilter f);
    Task<List<(string Id, string? FilePath)>> ScrollAllAsync(string coll);
    Task<Dictionary<string, (bool Pending, float[]? Summary)>> RetrieveAsync(string coll, IReadOnlyList<Guid> ids);
    Task DeleteAsync(string coll, IReadOnlyList<Guid> ids);
    Task<SchemaInfo> DescribeAsync(string coll);
    Task<IReadOnlyList<string>> ListCollectionsAsync();
    Task DropAsync(string coll);
}

public sealed record SchemaInfo(Dictionary<string, ulong> Dense, string[] Sparse, string[] IndexedPayload, long Points);

public sealed class QdrantStore : IStore
{
    public const string DenseName = "dense", SparseName = "sparse-code", SummaryName = "dense-resumen";
    public static readonly Guid ManifestPointId = new("00000000-0000-0000-0000-00000000ffff");
    private readonly QdrantClient _c;

    public QdrantStore(string host, int port) => _c = new QdrantClient(host, port);
    public QdrantClient Client => _c;
    public string Name => "qdrant";

    public async Task RecreateAsync(string coll, int dim, bool summary)
    {
        if (await _c.CollectionExistsAsync(coll)) await _c.DeleteCollectionAsync(coll);
        var v = new VectorParamsMap();
        v.Map[DenseName] = new VectorParams { Size = (ulong)dim, Distance = Distance.Cosine, OnDisk = false };
        if (summary) v.Map[SummaryName] = new VectorParams { Size = (ulong)dim, Distance = Distance.Cosine, OnDisk = false };
        var s = new SparseVectorConfig();
        s.Map[SparseName] = new SparseVectorParams { Index = new SparseIndexConfig { FullScanThreshold = 5000 } };
        await _c.CreateCollectionAsync(coll, vectorsConfig: v, sparseVectorsConfig: s);
        await _c.CreatePayloadIndexAsync(coll, "defined_symbols", PayloadSchemaType.Keyword);
    }

    public Task FlushAsync(string coll) => Task.CompletedTask; // Qdrant no expone flush: wait=true ya aplica al indice

    public async Task UpsertAsync(string coll, IReadOnlyList<Chunk> batch, bool wait = true)
    {
        var points = batch.Select(ToPoint).ToList();
        await _c.UpsertAsync(coll, points, wait: wait);
    }

    public static PointStruct ToPoint(Chunk c)
    {
        var p = new PointStruct { Id = new PointId { Uuid = c.Key } };
        var nv = new NamedVectors();
        var dv = new Vector();
        dv.Data.AddRange(c.Dense);
        nv.Vectors[DenseName] = dv;
        Vector sv = (c.SparseVal, c.SparseIdx);
        nv.Vectors[SparseName] = sv;
        if (c.Summary is { Length: > 0 }) nv.Vectors[SummaryName] = c.Summary;
        p.Vectors = new Vectors { Vectors_ = nv };
        p.Payload["content"] = c.Content;
        p.Payload["enriched_content"] = c.Content;
        p.Payload["file_path"] = c.FilePath;
        p.Payload["relative_path"] = c.RelativePath;
        p.Payload["language"] = c.Language;
        p.Payload["start_line"] = c.StartLine;
        p.Payload["end_line"] = c.EndLine;
        p.Payload["chunk_type"] = c.ChunkType;
        p.Payload["content_hash"] = c.ContentHash;
        p.Payload["last_modified"] = c.LastModified;
        p.Payload["repository_name"] = c.RepositoryName;
        if (c.Namespace is not null) p.Payload["namespace"] = c.Namespace;
        if (c.ClassName is not null) p.Payload["class_name"] = c.ClassName;
        if (c.MethodName is not null) p.Payload["method_name"] = c.MethodName;
        if (c.DefinedSymbols.Length > 0) p.Payload["defined_symbols"] = KeywordList(c.DefinedSymbols);
        if (c.ConsumedSymbols.Length > 0) p.Payload["consumed_symbols"] = KeywordList(c.ConsumedSymbols);
        if (!string.IsNullOrWhiteSpace(c.Tenant)) p.Payload["tenant"] = c.Tenant;
        p.Payload["resumen_pending"] = c.ResumenPending;
        return p;
    }

    private static Value KeywordList(IEnumerable<string> xs)
    {
        var l = new ListValue();
        l.Values.AddRange(xs.Select(x => new Value { StringValue = x }));
        return new Value { ListValue = l };
    }

    public async Task UpsertManifestAsync(string coll, string json, int dim)
    {
        var p = new PointStruct { Id = new PointId { Uuid = ManifestPointId.ToString() } };
        var dv = new Vector();
        dv.Data.AddRange(new float[dim]);
        var nv = new NamedVectors();
        nv.Vectors[DenseName] = dv;
        p.Vectors = new Vectors { Vectors_ = nv };
        p.Payload["is_manifest"] = true;
        p.Payload["__manifest__"] = json;
        await _c.UpsertAsync(coll, new[] { p }, wait: true);
    }

    public async Task<string?> GetManifestAsync(string coll)
    {
        var pts = await _c.RetrieveAsync(coll, new List<PointId> { new() { Uuid = ManifestPointId.ToString() } }, withPayload: true, withVectors: false);
        return pts.Count > 0 && pts[0].Payload.TryGetValue("__manifest__", out var v) ? v.StringValue : null;
    }

    public async Task<IReadOnlyList<Hit>> SearchAsync(string coll, Branch branch, Query q, EvalFilter f, ulong limit, float? threshold = null)
    {
        Qdrant.Client.Grpc.Query query = branch == Branch.Sparse
            ? (q.SparseVal, q.SparseIdx)
            : q.Dense;
        var vector = branch switch { Branch.Dense => DenseName, Branch.Sparse => SparseName, _ => SummaryName };
        var payload = f.Module is null
            ? new WithPayloadSelector { Enable = false }
            : new WithPayloadSelector { Include = new PayloadIncludeSelector { Fields = { "namespace", "relative_path" } } };
        var pts = await _c.QueryAsync(coll, query: query, usingVector: vector, filter: BuildFilter(f), limit: limit,
            payloadSelector: payload, searchParams: new SearchParams { Exact = true }, scoreThreshold: threshold);
        // Igual que QdrantSemanticRetriever: Match.Text prefiltra y MatchesModuleBoundary decide.
        return pts
            .Where(p => f.Module is null || Oracle.MatchesModuleBoundary(
                p.Payload.TryGetValue("namespace", out var ns) ? ns.StringValue : null,
                p.Payload.TryGetValue("relative_path", out var rp) ? rp.StringValue : "", f.Module))
            .Select(p => new Hit(Guid.Parse(p.Id.Uuid).ToString("D"), p.Score)).ToList();
    }

    public static Filter BuildFilter(EvalFilter f)
    {
        var must = new List<Condition>();
        if (f.Tenant is not null) must.Add(Conditions.MatchKeyword("tenant", f.Tenant));
        if (f.Language is not null)
            must.Add(new Condition { Field = new FieldCondition { Key = "language", Match = new Match { Text = f.Language } } });
        if (f.Module is not null)
        {
            must.Add(new Condition
            {
                Filter = new Filter
                {
                    Should =
                    {
                        new Condition { Field = new FieldCondition { Key = "namespace", Match = new Match { Text = f.Module } } },
                        new Condition { Field = new FieldCondition { Key = "relative_path", Match = new Match { Text = f.Module } } },
                    }
                }
            });
        }
        if (f.DefinedAny is not null) must.Add(Conditions.Match("defined_symbols", f.DefinedAny.ToList()));
        if (f.Pairs is not null)
        {
            var pairs = new Filter();
            foreach (var (cls, m) in f.Pairs)
            {
                var pair = new Filter();
                pair.Must.Add(Conditions.MatchKeyword("class_name", cls));
                pair.Must.Add(Conditions.Match("defined_symbols", new List<string> { m }));
                pairs.Should.Add(new Condition { Filter = pair });
            }
            must.Add(new Condition { Filter = pairs });
        }
        if (f.Ids is not null) must.Add(Conditions.HasId(f.Ids.ToList()));
        if (f.ResumenPendingOnly)
            must.Add(new Condition { Field = new FieldCondition { Key = "resumen_pending", Match = new Match { Boolean = true } } });

        var excludeManifest = new Condition { Field = new FieldCondition { Key = "is_manifest", Match = new Match { Boolean = true } } };
        var filter = new Filter { MustNot = { excludeManifest } };
        filter.Must.AddRange(must);
        return filter;
    }

    public async Task UpdateSummaryVectorAsync(string coll, Guid id, float[] v)
    {
        var nv = new NamedVectors();
        nv.Vectors[SummaryName] = v;
        await _c.UpdateVectorsAsync(coll, new[] { new PointVectors { Id = new PointId { Uuid = id.ToString() }, Vectors = new Vectors { Vectors_ = nv } } }, wait: true);
    }

    public async Task MarkResumenCompleteAsync(string coll, IReadOnlyList<Guid> ids)
    {
        var payload = new Dictionary<string, Value> { ["resumen_pending"] = false };
        await _c.SetPayloadAsync(coll, payload, ids: ids.ToList(), wait: true);
    }

    public async Task<long> CountAsync(string coll, EvalFilter f) => (long)await _c.CountAsync(coll, BuildFilter(f), exact: true);

    public async Task<List<(string Id, string? FilePath)>> ScrollAllAsync(string coll)
    {
        var all = new List<(string, string?)>();
        PointId? offset = null;
        while (true)
        {
            var r = await _c.ScrollAsync(coll, limit: 512, offset: offset,
                payloadSelector: new WithPayloadSelector { Include = new PayloadIncludeSelector { Fields = { "file_path" } } },
                vectorsSelector: new WithVectorsSelector { Enable = false });
            foreach (var p in r.Result)
                all.Add((Guid.Parse(p.Id.Uuid).ToString("D"), p.Payload.TryGetValue("file_path", out var fp) ? fp.StringValue : null));
            var next = r.NextPageOffset;
            if (next is null || next.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.None) break;
            offset = next;
        }
        return all;
    }

    public async Task<Dictionary<string, (bool Pending, float[]? Summary)>> RetrieveAsync(string coll, IReadOnlyList<Guid> ids)
    {
        var pts = await _c.RetrieveAsync(coll, ids.Select(i => new PointId { Uuid = i.ToString() }).ToList(), withPayload: true, withVectors: true);
        var res = new Dictionary<string, (bool, float[]?)>();
        foreach (var p in pts)
        {
            var pending = p.Payload.TryGetValue("resumen_pending", out var v) && v.BoolValue;
            float[]? summary = null;
            if (p.Vectors.VectorsOptionsCase == VectorsOutput.VectorsOptionsOneofCase.Vectors &&
                p.Vectors.Vectors.Vectors.TryGetValue(SummaryName, out var vo))
            {
                var data = vo.VectorCase == VectorOutput.VectorOneofCase.Dense ? vo.Dense.Data : vo.Data;
                summary = data.Count > 0 ? data.ToArray() : null;
            }
            res[Guid.Parse(p.Id.Uuid).ToString("D")] = (pending, summary);
        }
        return res;
    }

    public async Task DeleteAsync(string coll, IReadOnlyList<Guid> ids) => await _c.DeleteAsync(coll, ids.ToList(), wait: true);

    public async Task<SchemaInfo> DescribeAsync(string coll)
    {
        var info = await _c.GetCollectionInfoAsync(coll);
        var dense = new Dictionary<string, ulong>();
        if (info.Config.Params.VectorsConfig.ConfigCase == VectorsConfig.ConfigOneofCase.ParamsMap)
            foreach (var (n, p) in info.Config.Params.VectorsConfig.ParamsMap.Map) dense[n] = p.Size;
        var sparse = info.Config.Params.SparseVectorsConfig?.Map.Keys.ToArray() ?? Array.Empty<string>();
        return new SchemaInfo(dense, sparse, info.PayloadSchema.Keys.ToArray(), (long)info.PointsCount);
    }

    public async Task<IReadOnlyList<string>> ListCollectionsAsync() => await _c.ListCollectionsAsync();
    public async Task DropAsync(string coll) { if (await _c.CollectionExistsAsync(coll)) await _c.DeleteCollectionAsync(coll); }

    /// <summary>Exporta todos los puntos (vectores + payload) en paginas: base de la migracion medida en el criterio 2.</summary>
    public async IAsyncEnumerable<(List<Chunk> Chunks, string? Manifest)> ExportAsync(string coll, uint page = 256)
    {
        PointId? offset = null;
        while (true)
        {
            var r = await _c.ScrollAsync(coll, limit: page, offset: offset,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = true });
            var chunks = new List<Chunk>();
            string? manifest = null;
            foreach (var p in r.Result)
            {
                if (p.Payload.TryGetValue("__manifest__", out var m)) { manifest = m.StringValue; continue; }
                chunks.Add(FromRetrieved(p));
            }
            yield return (chunks, manifest);
            var next = r.NextPageOffset;
            if (next is null || next.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.None) break;
            offset = next;
        }
    }

    private static Chunk FromRetrieved(RetrievedPoint p)
    {
        var vs = p.Vectors.Vectors.Vectors;
        float[] Dense(VectorOutput vo) => (vo.VectorCase == VectorOutput.VectorOneofCase.Dense ? vo.Dense.Data : vo.Data).ToArray();
        var sv = vs[SparseName];
        uint[] sIdx;
        float[] sVal;
        if (sv.VectorCase == VectorOutput.VectorOneofCase.Sparse) { sIdx = sv.Sparse.Indices.ToArray(); sVal = sv.Sparse.Values.ToArray(); }
        else { sIdx = sv.Indices.Data.ToArray(); sVal = sv.Data.ToArray(); }
        var pl = p.Payload;
        string? S(string k) => pl.TryGetValue(k, out var v) ? v.StringValue : null;
        string[] L(string k) => pl.TryGetValue(k, out var v) && v.KindCase == Value.KindOneofCase.ListValue
            ? v.ListValue.Values.Select(x => x.StringValue).ToArray() : Array.Empty<string>();
        return new Chunk(
            Guid.Parse(p.Id.Uuid), Dense(vs[DenseName]), sIdx, sVal,
            vs.TryGetValue(SummaryName, out var sm) ? Dense(sm) : null,
            pl.TryGetValue("resumen_pending", out var rp) && rp.BoolValue,
            S("content")!, S("file_path")!, S("relative_path")!, S("language")!, S("namespace"), S("class_name"), S("method_name"),
            (int)pl["start_line"].IntegerValue, (int)pl["end_line"].IntegerValue, S("chunk_type")!, S("content_hash")!,
            S("last_modified")!, S("repository_name")!, L("defined_symbols"), L("consumed_symbols"), S("tenant"));
    }
}
