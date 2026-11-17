using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

public sealed class RequiereQdrantTextualFactAttribute : FactAttribute
{
    public RequiereQdrantTextualFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VSE_QDRANT_TEXTUAL") != "1")
            Skip = "Requiere el banco efimero 1.21.4 en 127.0.0.1:26333/26334.";
    }
}

/// <summary>
/// Oraculo fijado antes de medir: Match.Text directo por REST (no constructor productivo), luego predicados
/// independientes de modulo/simbolos y ranking por vectores guardados. Sin indices full-text, Qdrant 1.19.2.
/// Los 120 negativos superan el pool minimo de 40 de ambas ramas. Todo vive en it-1-21-4-*.
/// </summary>
[Collection("Api")]
public sealed class FiltrosTextualesIntegracionTests(ApiFactory factory)
{
    private const string Consulta = "texto prueba";
    private const string PerfilResumen = "it-1-21-4-resumen";
    private const float Umbral = 0.25f;
    private readonly QdrantClient _qdrant = new("127.0.0.1", 26334);
    private readonly HttpClient _rest = new() { BaseAddress = new Uri("http://127.0.0.1:26333/") };
    private readonly ContadorSintetico _contador = new();

    private sealed record Caso(string? Lenguaje, string? Namespace, string[]? Modulos = null, string[]? Simbolos = null);
    private static readonly Caso[] Casos =
    [
        new("CSharp", null), new("Markdown", null), new("Unknown", null),
        new(null, "alpha"), new(null, "ALPHA"), new(null, "alp"), new(null, " Alpha "),
        new(null, "alpha.Foo"), new(null, "alpha..Foo"), new(null, "alpha/beta"),
        new(null, "Reyma.Ventas"), new(null, "reyma.ventas"),
        new("CSharp", "alpha"), new("CSharp", "alpha", ["alpha", "tests"], ["S1", "S2"]),
        new("CSharp", "alpha", ["alpha", "imposible"], ["S1", "S2"]),
        new("Markdown", "beta"), new(null, null), new(null, ""), new(null, " \t\r\n")
    ];

    private WebApplicationFactory<Program> Api(string modo) =>
        factory.ConIdentidad(new Dictionary<string, string?>
        {
            ["Motor:Qdrant:Host"] = "127.0.0.1", ["Motor:Qdrant:PuertoGrpc"] = "26334",
            ["Motor:Qdrant:PuertoRest"] = "26333", ["Motor:Qdrant:ApiKey"] = "", ["Motor:Qdrant:Https"] = "false",
            ["Motor:Embeddings:UrlBase"] = "http://127.0.0.1:9", ["Motor:Embeddings:Dimension"] = "4",
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:UrlBase"] = "http://127.0.0.1:9",
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:Modelo"] = "sintetico-resumen",
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:Dimension"] = "4",
            ["Motor:Llm:UrlBase"] = "http://127.0.0.1:9", ["Motor:Resumen:PerfilEmbeddings"] = PerfilResumen,
            ["Motor:Busqueda:Modo"] = modo, ["Motor:Busqueda:UmbralSimilitud"] = "0.25",
            ["Motor:Busqueda:UmbralSimilitudResumen"] = "0.25"
        }, servicios: s =>
        {
            s.AddSingleton(_contador);
            s.AddSingleton<IVectorizationBrainFactory, FabricaDeVectoresSintetica>();
            s.AddSingleton<ILlmResumenBrain>(new LlmSintetico(_contador));
        });

    private static string Id(int n) => $"00000000-0000-0000-0000-{n:D12}";

    private static PointStruct Punto(int n, string? lenguaje, string? ns, string ruta, float score,
        string? simbolo = "S1", bool resumen = true)
    {
        var p = new PointStruct { Id = new PointId { Uuid = Id(n) } };
        var vector = new VectoresSinteticos("fixture", 4, new ContadorSintetico());
        var sparse = new SparseTokenizer().Tokenize(Consulta);
        var vectores = new NamedVectors();
        vectores.Vectors["dense"] = vector.Vector(score);
        vectores.Vectors["sparse-code"] = (sparse.Select(_ => score * 10).ToArray(), sparse.Select(t => t.TermIndex).ToArray());
        if (resumen) vectores.Vectors["dense-resumen"] = vector.Vector(score - 0.02f);
        p.Vectors = new Vectors { Vectors_ = vectores };
        p.Payload["content"] = $"fixture {n}";
        p.Payload["relative_path"] = ruta;
        p.Payload["content_hash"] = $"hash-{n}";
        p.Payload["document_fingerprint"] = $"huella-{n}";
        if (lenguaje is not null) p.Payload["language"] = lenguaje;
        if (ns is not null) p.Payload["namespace"] = ns;
        if (simbolo is not null) p.Payload["defined_symbols"] = ModulePrefixes.ToPayloadValue([simbolo]);
        p.Payload[ModulePrefixes.PayloadKey] = ModulePrefixes.ToPayloadValue(ModulePrefixes.Derive(ns, ruta));
        p.Payload[ModulePrefixes.VersionPayloadKey] = new Value { IntegerValue = ModulePrefixes.Version };
        return p;
    }

    private static List<PointStruct> Corpus()
    {
        List<PointStruct> puntos =
        [
            Punto(1, "CSharp", "alpha", "tests/a.cs", .60f),
            Punto(2, "csharp", "alpha.Foo", "tests/b.cs", .55f, "S2"),
            Punto(3, "CSharp", "Company.alpha", "tests/c.cs", .50f),
            Punto(4, "CSharp", "Alpha", "tests/d.cs", .48f, "S2"),
            Punto(5, "CSharp", "alpha..Foo", "tests/e.cs", .46f),
            Punto(6, "CSharp", "alpha/beta", "tests/f.cs", .44f, "S2"),
            Punto(7, "CSharp", " alpha ", "tests/g.cs", .42f),
            Punto(8, "CSharp", "beta", "alpha/h.cs", .99f),
            Punto(9, "CSharp", null, "alpha/i.cs", .98f),
            Punto(10, null, "alpha", "tests/j.cs", .97f),
            Punto(11, "Markdown", "alpha", "tests/k.md", .96f),
            Punto(12, "CSharp", "alpha", "otro/l.cs", .95f),
            Punto(13, "CSharp", "alpha", "tests/m.cs", .94f, "S3"),
            Punto(14, "CSharp", "alpha", "tests/n.cs", .93f, null),
            Punto(15, "CSharp", "alpha", "tests/bajo.cs", .20f),
            Punto(16, "CSharp", "alpha", "tests/sin-resumen.cs", .65f, resumen: false),
            Punto(201, "CSharp", "Reyma.Ventas.Api", "historia/a.cs", .40f),
            Punto(202, "CSharp", "Reyma.VentasExtra.Api", "historia/b.cs", .41f),
            Punto(203, "CSharp", "xReyma.Ventas", "historia/c.cs", .42f)
        ];
        for (var i = 0; i < 120; i++)
            puntos.Add(Punto(30 + i, i % 2 == 0 ? "Markdown" : "CSharp",
                i % 2 == 0 ? "alpha" : "beta", $"tests/negativo{i}.cs", .90f));
        return puntos;
    }

    private async Task<string> Crear(WebApplicationFactory<Program> api)
    {
        var version = JObject.Parse(await _rest.GetStringAsync(""));
        Assert.Equal("1.19.2", (string?)version["version"]);
        var nombre = "it-1-21-4-" + Guid.NewGuid().ToString("N");
        await api.Services.GetRequiredService<QdrantVectorStore>().EnsureCollectionAsync(
            nombre, MotorOptions.PerfilEmbeddingsPorDefecto, "sintetico-codigo", 4, true, PerfilResumen, 4, default);
        await _qdrant.UpsertAsync(nombre, Corpus(), wait: true);
        await SinFullText(nombre);
        return nombre;
    }

    private async Task SinFullText(string nombre)
    {
        var info = await _qdrant.GetCollectionInfoAsync(nombre);
        Assert.DoesNotContain("language", info.PayloadSchema.Keys);
        Assert.DoesNotContain("namespace", info.PayloadSchema.Keys);
    }

    private async Task<List<RetrievedPoint>> Puntos(string nombre) =>
        (await _qdrant.ScrollAsync(nombre, limit: 1000, payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: new WithVectorsSelector { Enable = true })).Result.ToList();

    // REST independiente: las claves/valores son los dos Match.Text observados en BuildFilter del origen.
    private async Task<HashSet<string>> ReferenciaTextual(string nombre, Caso caso)
    {
        var must = new List<object>();
        if (caso.Lenguaje is not null) must.Add(new { key = "language", match = new { text = caso.Lenguaje } });
        if (!string.IsNullOrWhiteSpace(caso.Namespace)) must.Add(new { key = "namespace", match = new { text = caso.Namespace } });
        using var r = await _rest.PostAsJsonAsync($"collections/{nombre}/points/scroll",
            new { limit = 1000, with_payload = false, filter = new { must } });
        r.EnsureSuccessStatusCode();
        var result = JObject.Parse(await r.Content.ReadAsStringAsync())["result"]!;
        Assert.True(result["next_page_offset"]!.Type == JTokenType.Null, "El oraculo no puede truncar puntos.");
        return result["points"]!.Select(p => (string)p["id"]!).ToHashSet(StringComparer.Ordinal);
    }

    private static string? Texto(RetrievedPoint p, string key) => p.Payload.GetValueOrDefault(key)?.StringValue;

    private static List<(string Id, float Score)> Ranking(List<RetrievedPoint> puntos, HashSet<string> ids, Caso caso, int k, string modo, string espacio)
    {
        var elegibles = puntos.Where(p => ids.Contains(p.Id.Uuid))
            .Where(p => caso.Modulos is null || caso.Modulos.All(m =>
                FiltroDeModuloContratoTests.Predicado(m, Texto(p, "namespace"), Texto(p, "relative_path"))))
            .Where(p => caso.Simbolos is null || (p.Payload.TryGetValue("defined_symbols", out var s) &&
                s.ListValue.Values.Any(v => caso.Simbolos.Contains(v.StringValue, StringComparer.Ordinal)))).ToList();
        var vector = espacio == "resumen" ? "dense-resumen" : "dense";
        var denso = elegibles.Where(p => p.Vectors.Vectors.Vectors.ContainsKey(vector))
            .Select(p => (Id: p.Id.Uuid, Score: p.Vectors.Vectors.Vectors[vector].Dense.Data[0]))
            .Where(p => p.Score > Umbral).OrderByDescending(p => p.Score).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        if (modo == "Denso" || espacio == "resumen") return denso.Take(k).ToList();
        var consulta = new SparseTokenizer().Tokenize(Consulta).ToDictionary(t => t.TermIndex, t => t.Weight);
        var sparse = elegibles.Select(p =>
        {
            var v = p.Vectors.Vectors.Vectors["sparse-code"].Sparse;
            var score = 0f;
            for (var i = 0; i < v.Indices.Count; i++) score += consulta.GetValueOrDefault(v.Indices[i]) * v.Values[i];
            return (Id: p.Id.Uuid, Score: score);
        }).Where(p => p.Score > 0).OrderByDescending(p => p.Score).ThenBy(p => p.Id, StringComparer.Ordinal);
        var scores = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var rama in new[] { denso.AsEnumerable(), sparse })
        {
            var rank = 0;
            foreach (var p in rama.Take(Math.Max(4 * k, 40)))
                scores[p.Id] = scores.GetValueOrDefault(p.Id) + 1f / (rank++ + 2);
        }
        return scores.Select(p => (Id: p.Key, Score: p.Value)).OrderByDescending(p => p.Score)
            .ThenBy(p => p.Id, StringComparer.Ordinal).Take(k).ToList();
    }

    private static async Task<List<(string Id, float Score)>> Buscar(HttpClient client, string nombre, Caso caso, int k, string espacio)
    {
        using var r = await client.PostJson($"/api/colecciones/{nombre}/busquedas",
            new { consulta = Consulta, topK = k, lenguaje = caso.Lenguaje, @namespace = caso.Namespace,
                modulos = caso.Modulos, simbolosDefinidos = caso.Simbolos, espacio });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Leer<JObject>())["resultados"]!.Select(p => ((string)p["id"]!, (float)p["puntuacion"]!)).ToList();
    }

    private static void Iguales(List<(string Id, float Score)> esperado, List<(string Id, float Score)> actual, bool exacto = false)
    {
        Assert.Equal(esperado.Select(p => p.Id), actual.Select(p => p.Id));
        for (var i = 0; i < esperado.Count; i++)
            if (exacto) Assert.Equal(esperado[i].Score, actual[i].Score);
            else Assert.Equal(esperado[i].Score, actual[i].Score, 5);
    }

    private async Task Historia(string nombre)
    {
        foreach (var texto in new[] { "Reyma.Ventas", "reyma.ventas" })
            Assert.Equal([Id(201)], (await ReferenciaTextual(nombre, new(null, texto))).OrderBy(x => x));
    }

    [RequiereQdrantTextualFact]
    public async Task ReferenciaHistoricaCoincideSinModificarOraculo()
    {
        var api = Api("Denso");
        var nombre = await Crear(api);
        try { await Historia(nombre); }
        finally { await _qdrant.DeleteCollectionAsync(nombre); }
    }

    private async Task Matriz(string modo, string espacio)
    {
        var api = Api(modo);
        using var client = api.Cliente(Token(sub: SubAna));
        var nombre = await Crear(api);
        try
        {
            await Historia(nombre);
            var puntos = await Puntos(nombre);
            var esquema = (await _qdrant.GetCollectionInfoAsync(nombre)).PayloadSchema.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
            var positivo = await ReferenciaTextual(nombre, new("CSharp", "alpha"));
            Assert.Contains(Id(1), positivo);
            Assert.Contains(Id(2), positivo); // Match.Text no es igualdad keyword de lenguaje.
            foreach (var id in new[] { 8, 9, 10, 11 }) Assert.DoesNotContain(Id(id), positivo);
            foreach (var caso in Casos)
            {
                var ids = await ReferenciaTextual(nombre, caso);
                foreach (var k in new[] { 3, 100 })
                    Iguales(Ranking(puntos, ids, caso, k, modo, espacio), await Buscar(client, nombre, caso, k, espacio),
                        exacto: modo == "FusionNativa" && espacio == "codigo");
            }
            var riesgo = new Caso("CSharp", "alpha", ["alpha", "tests"], ["S1", "S2"]);
            var esperados = Ranking(puntos, await ReferenciaTextual(nombre, riesgo), riesgo, 3, modo, espacio);
            Assert.Equal(3, esperados.Count);
            Iguales(esperados, await Buscar(client, nombre, riesgo, 3, espacio));
            var sinFiltro = await Buscar(client, nombre, new(null, null), 3, espacio);
            Assert.DoesNotContain(sinFiltro, p => esperados.Any(e => e.Id == p.Id));
            Assert.Equal(puntos, await Puntos(nombre));
            Assert.Equal(esquema, (await _qdrant.GetCollectionInfoAsync(nombre)).PayloadSchema.OrderBy(p => p.Key, StringComparer.Ordinal));
            await SinFullText(nombre);
            Assert.Equal(0, _contador.Llm);
        }
        finally { await _qdrant.DeleteCollectionAsync(nombre); }
    }

    [RequiereQdrantTextualFact]
    public Task DensoAplicaTextoAntesDelCorteYAndConCadaModuloYAnySimbolos() => Matriz("Denso", "codigo");

    [RequiereQdrantTextualFact]
    public Task FusionFiltraDensoYSparseAntesDelPool() => Matriz("FusionNativa", "codigo");

    [RequiereQdrantTextualFact]
    public async Task ResumenAplicaMismoFiltroAntesDelCorteEnAmbosModos()
    {
        await Matriz("Denso", "resumen");
        await Matriz("FusionNativa", "resumen");
    }

    [RequiereQdrantTextualFact]
    public async Task SinFiltrosNuevosConservaIdsOrdenYScoresDelLectorAnterior()
    {
        foreach (var modo in new[] { "Denso", "FusionNativa" })
        {
            var api = Api(modo);
            using var client = api.Cliente(Token(sub: SubAna));
            var nombre = await Crear(api);
            try
            {
                var s = api.Services;
                var anterior = new LectorAnterior1_21_4(s.GetRequiredService<QdrantClient>(),
                    s.GetRequiredService<IVectorizationBrainFactory>(), s.GetRequiredService<QdrantVectorStore>(),
                    s.GetRequiredService<ISparseTokenizer>(), s.GetRequiredService<IOptions<MotorOptions>>(),
                    NullLogger<LectorAnterior1_21_4>.Instance);
                foreach (var espacio in new[] { "codigo", "resumen" })
                foreach (var caso in new[] { new Caso(null, null), new(null, "", ["alpha", "tests"], ["S1", "S2"]),
                    new(null, " \t", ["alpha"], ["S2"]) })
                foreach (var k in new[] { 3, 100 })
                {
                    var esperado = await anterior.SearchAsync(nombre, Consulta, k, null, caso.Simbolos,
                        espacio == "resumen" ? EspacioDeBusqueda.Resumen : EspacioDeBusqueda.Codigo, caso.Modulos, default);
                    Iguales(esperado.Select(p => (p.ChunkId, p.Score)).ToList(), await Buscar(client, nombre, caso, k, espacio), exacto: true);
                }
            }
            finally { await _qdrant.DeleteCollectionAsync(nombre); }
        }
    }
}
