using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Item 1.21.1: filtro exacto <c>modulos</c> y relleno de <c>module_prefixes</c> contra un Qdrant local real, con
/// embeddings y LLM inyectados (<see cref="FabricaDeVectoresSintetica"/>, <see cref="LlmSintetico"/>: 0 red, 0 tokens).
/// El score de cada chunk lo fija el marcador <c>score=0.NN</c> de su texto, asi que los falsos candidatos se pueden
/// poner por encima de los elegibles. El oraculo es independiente del indice: lee los puntos guardados, evalua el
/// predicado literal de la ficha sobre <c>namespace</c>/<c>relative_path</c> (no sobre los prefijos) y ordena por
/// dot(consulta, vector guardado) desc y luego id ordinal, con el mismo umbral. Colecciones <c>it-1-21-*</c>, borradas
/// al terminar.
/// </summary>
[Collection("Api")]
public sealed class FiltroDeModuloIntegracionTests(ApiFactory factory)
{
    private const string PerfilResumen = "it-1-21-resumen";
    private const float Umbral = 0.25f;
    private const int DimensionCodigo = 16;
    private const int DimensionResumen = 8;
    private static readonly string Caido = $"http://127.0.0.1:{ApiFactory.SinServicio}";
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");

    private readonly QdrantClient _qdrant = new("127.0.0.1", 16334);
    private readonly HttpClient _rest = new() { BaseAddress = new Uri("http://127.0.0.1:16333/") };
    private readonly ContadorSintetico _contador = new();

    // El metodo supera MinIndexableContentChars (60) del pipeline; la declaracion de clase sola no se indexa.
    private static string Cs(string ns, string clase, double score) =>
        $"namespace {ns};\n\npublic class {clase}\n{{\n    // score={Fmt(score)}\n    public int M{clase}() => 1; // score={Fmt(score)} metodo sintetico del filtro de modulo\n}}\n";

    private static string Md(double score) => $"# Nota\n\nTexto de prueba score={Fmt(score)} para el filtro de modulo.\n";

    private static string Fmt(double s) => s.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Positivos para "alpha" (P*) por debajo de negativos (N*) que llenarian el top-K sin filtro previo.</summary>
    private static readonly (string Ruta, string Contenido)[] Corpus =
    [
        ("x/a1.cs", Cs("alpha", "A1", 0.50)),
        ("x/a2.cs", Cs("alpha.Foo", "A2", 0.45)),
        ("alpha/child/Foo.cs", Cs("other.Space", "A3", 0.40)),
        ("alpha/p4.md", Md(0.35)),
        ("alpha/bajo.md", Md(0.20)), // elegible pero bajo el umbral
        ("y/n1.cs", Cs("alpha_private", "N1", 0.95)),
        ("y/n2.cs", Cs("Alpha", "N2", 0.94)),
        ("y/n3.cs", Cs("Company.alpha", "N3", 0.93)),
        ("src/alpha/Foo.cs", Cs("zeta", "N4", 0.92)),
        ("readme.md", Md(0.91)),
        ("alpha-private/x.md", Md(0.90)),
        ("alphabet/x.md", Md(0.89)),
        ("Alpha/x.md", Md(0.88)),
    ];

    private WebApplicationFactory<Program> Api(string modo = "Denso") =>
        factory.ConIdentidad(new Dictionary<string, string?>
        {
            ["Motor:Qdrant:Host"] = "127.0.0.1",
            ["Motor:Qdrant:PuertoGrpc"] = "16334",
            ["Motor:Qdrant:PuertoRest"] = "16333",
            ["Motor:Qdrant:ApiKey"] = "",
            ["Motor:Qdrant:Https"] = "false",
            ["Motor:Embeddings:UrlBase"] = Caido,
            ["Motor:Embeddings:Dimension"] = DimensionCodigo.ToString(CultureInfo.InvariantCulture),
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:UrlBase"] = Caido,
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:Modelo"] = "sintetico-resumen",
            [$"Motor:Embeddings:Perfiles:{PerfilResumen}:Dimension"] = DimensionResumen.ToString(CultureInfo.InvariantCulture),
            ["Motor:Llm:UrlBase"] = Caido,
            ["Motor:Resumen:PerfilEmbeddings"] = PerfilResumen,
            ["Motor:Resumen:TopeTokensPorCorrida"] = "100000",
            ["Motor:Busqueda:Modo"] = modo,
            ["Motor:Busqueda:UmbralSimilitud"] = Umbral.ToString(CultureInfo.InvariantCulture),
            ["Motor:Busqueda:UmbralSimilitudResumen"] = Umbral.ToString(CultureInfo.InvariantCulture),
        }, servicios: s =>
        {
            s.AddSingleton(_contador);
            s.AddSingleton<IVectorizationBrainFactory, FabricaDeVectoresSintetica>();
            s.AddSingleton<ILlmResumenBrain>(new LlmSintetico(_contador));
        });

    private static string NuevaColeccion() => "it-1-21-" + Guid.NewGuid().ToString("N")[..10];

    private async Task Borrar(params string[] colecciones)
    {
        foreach (var c in colecciones)
            if (await _qdrant.CollectionExistsAsync(c))
                await _qdrant.DeleteCollectionAsync(c);
    }

    private RellenoDePrefijosDeModulo Relleno(Func<int, CancellationToken, Task>? trasPagina = null, TimeSpan? lease = null) =>
        new(_qdrant, _rest, NullLogger.Instance) { TrasPagina = trasPagina, Lease = lease ?? RellenoDePrefijosDeModulo.LeasePorDefecto };

    private static async Task<HttpResponseMessage> PostIngesta(HttpClient client, string coleccion, bool resumen, IEnumerable<(string Ruta, string Contenido)> docs) =>
        await client.PostJson($"/api/colecciones/{coleccion}/ingestas", new
        {
            origen = new { tipo = "documentos", documentos = docs.Select(d => new { ruta = d.Ruta, contenido = d.Contenido }) },
            resumen
        });

    private static async Task Ingerir(HttpClient client, string coleccion, IEnumerable<(string Ruta, string Contenido)> docs, bool resumen = false)
    {
        using var response = await PostIngesta(client, coleccion, resumen, docs);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static Task<HttpResponseMessage> PostBusqueda(HttpClient client, string coleccion, int topK, string[]? modulos = null,
        string[]? simbolos = null, string? espacio = null)
    {
        var cuerpo = new Dictionary<string, object> { ["consulta"] = "texto de prueba nota valor modulo", ["topK"] = topK };
        if (modulos is not null) cuerpo["modulos"] = modulos;
        if (simbolos is not null) cuerpo["simbolosDefinidos"] = simbolos;
        if (espacio is not null) cuerpo["espacio"] = espacio;
        return client.PostJson($"/api/colecciones/{coleccion}/busquedas", cuerpo);
    }

    private static async Task<List<(string Id, float Score)>> Buscar(HttpClient client, string coleccion, int topK, string[]? modulos = null,
        string[]? simbolos = null, string? espacio = null)
    {
        using var response = await PostBusqueda(client, coleccion, topK, modulos, simbolos, espacio);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return ((JArray)(await response.Leer<JObject>())["resultados"]!)
            .Select(r => ((string)r["id"]!, (float)r["puntuacion"]!)).ToList();
    }

    private async Task<List<RetrievedPoint>> Puntos(string coleccion)
    {
        var todos = new List<RetrievedPoint>();
        PointId? offset = null;
        while (true)
        {
            var r = await _qdrant.ScrollAsync(coleccion, limit: 256, offset: offset,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = true });
            todos.AddRange(r.Result);
            if (r.NextPageOffset is null || r.NextPageOffset.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.None) break;
            offset = r.NextPageOffset;
        }
        return todos;
    }

    private static string? Cadena(RetrievedPoint p, string clave) =>
        p.Payload.TryGetValue(clave, out var v) && v.KindCase == Value.KindOneofCase.StringValue ? v.StringValue : null;

    private static IReadOnlyList<string> Lista(RetrievedPoint p, string clave) =>
        p.Payload.TryGetValue(clave, out var v) && v.KindCase == Value.KindOneofCase.ListValue
            ? v.ListValue.Values.Select(x => x.StringValue).ToList()
            : [];

    private static bool Elegible(RetrievedPoint p, IReadOnlyList<string> modulos) =>
        modulos.All(m => FiltroDeModuloContratoTests.Predicado(m, Cadena(p, "namespace"), Cadena(p, "relative_path")));

    /// <summary>Oraculo denso: predicado original + dot(e0, vector guardado), umbral, score desc y luego id ordinal.</summary>
    private static List<(string Id, float Score)> Oraculo(IEnumerable<RetrievedPoint> puntos, IReadOnlyList<string> modulos, int topK,
        string vector = "dense", IReadOnlyList<string>? simbolos = null) =>
        puntos
            .Where(p => Elegible(p, modulos))
            .Where(p => simbolos is null || Lista(p, "defined_symbols").Intersect(simbolos, StringComparer.Ordinal).Any())
            .Where(p => p.Vectors.Vectors.Vectors.ContainsKey(vector))
            .Select(p => (Id: p.Id.Uuid, Score: p.Vectors.Vectors.Vectors[vector].Dense.Data[0]))
            .Where(x => x.Score >= Umbral)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Take(topK).ToList();

    private static void IgualAlOraculo(List<(string Id, float Score)> esperado, List<(string Id, float Score)> real)
    {
        Assert.Equal(esperado.Select(e => e.Id), real.Select(r => r.Id));
        for (var i = 0; i < esperado.Count; i++)
            Assert.Equal(esperado[i].Score, real[i].Score, 4);
    }

    private static string Ruta(List<RetrievedPoint> puntos, string id) =>
        Cadena(puntos.Single(p => p.Id.Uuid == id), "relative_path")!;

    private async Task<string?> Marca(string coleccion)
    {
        using var doc = JsonDocument.Parse(await _rest.GetStringAsync($"collections/{coleccion}"));
        var metadata = doc.RootElement.GetProperty("result").GetProperty("config").GetProperty("metadata");
        return metadata.TryGetProperty(ModulePrefixes.MetadataKey, out var m) ? m.GetString() : null;
    }

    private async Task EscribirMarca(string coleccion, string? valor)
    {
        using var r = await _rest.PatchAsJsonAsync($"collections/{coleccion}",
            new { metadata = new Dictionary<string, object?> { [ModulePrefixes.MetadataKey] = valor } });
        r.EnsureSuccessStatusCode();
    }

    /// <summary>Deja la coleccion como la habria dejado el escritor anterior a 1.21.1: sin campos, indices ni marca.</summary>
    private async Task VolverAntigua(string coleccion)
    {
        var ids = (await Puntos(coleccion)).Select(p => Guid.Parse(p.Id.Uuid)).ToList();
        await _qdrant.DeletePayloadAsync(coleccion, [ModulePrefixes.PayloadKey, ModulePrefixes.VersionPayloadKey], ids, wait: true);
        await _qdrant.DeletePayloadIndexAsync(coleccion, ModulePrefixes.PayloadKey, wait: true);
        await _qdrant.DeletePayloadIndexAsync(coleccion, ModulePrefixes.VersionPayloadKey, wait: true);
        await EscribirMarca(coleccion, null);
        Assert.Null(await Marca(coleccion));
        Assert.Equal((ulong)ids.Count, await ModulePrefixes.ContarSinPrepararAsync(_qdrant, coleccion, CancellationToken.None));
    }

    /// <summary>Punto escrito por un escritor antiguo: payload minimo, sin <c>module_prefixes</c>.</summary>
    private async Task UpsertAntiguo(string coleccion, Guid id, string? ns, string ruta, float score)
    {
        var punto = new PointStruct { Id = new PointId { Uuid = id.ToString() } };
        var vectores = new NamedVectors();
        vectores.Vectors["dense"] = new VectoresSinteticos("x", DimensionCodigo, new ContadorSintetico()).Vector(score);
        punto.Vectors = new Vectors { Vectors_ = vectores };
        punto.Payload["content"] = $"antiguo score={Fmt(score)}";
        punto.Payload["relative_path"] = ruta;
        punto.Payload["file_path"] = ruta;
        punto.Payload["document_fingerprint"] = "antigua";
        if (ns is not null)
            punto.Payload["namespace"] = ns;
        await _qdrant.UpsertAsync(coleccion, [punto], wait: true);
    }

    /// <summary>Instantanea completa por punto: vectores y payload, cada uno serializado de forma estable.</summary>
    private async Task<SortedDictionary<string, (string Vectores, SortedDictionary<string, string> Payload)>> Instantanea(string coleccion)
    {
        var r = new SortedDictionary<string, (string, SortedDictionary<string, string>)>(StringComparer.Ordinal);
        foreach (var p in await Puntos(coleccion))
        {
            var vectores = string.Join("|", p.Vectors.Vectors.Vectors.OrderBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => v.Key + "=" + v.Value));
            var payload = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in p.Payload)
                payload[k] = v.ToString();
            r[p.Id.Uuid] = (vectores, payload);
        }
        return r;
    }

    private static void SoloCambiaronLosCamposDeModulo(
        SortedDictionary<string, (string Vectores, SortedDictionary<string, string> Payload)> antes,
        SortedDictionary<string, (string Vectores, SortedDictionary<string, string> Payload)> despues)
    {
        Assert.Equal(antes.Keys, despues.Keys);
        foreach (var id in antes.Keys)
        {
            Assert.Equal(antes[id].Vectores, despues[id].Vectores);
            var sinModulo = despues[id].Payload.Where(kv => kv.Key is not ModulePrefixes.PayloadKey and not ModulePrefixes.VersionPayloadKey)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            var antesSinModulo = antes[id].Payload.Where(kv => kv.Key is not ModulePrefixes.PayloadKey and not ModulePrefixes.VersionPayloadKey)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            Assert.Equal(antesSinModulo.OrderBy(kv => kv.Key, StringComparer.Ordinal), sinModulo.OrderBy(kv => kv.Key, StringComparer.Ordinal));
        }
    }

    private static void PrefijosCorrectos(IEnumerable<RetrievedPoint> puntos)
    {
        foreach (var p in puntos)
        {
            Assert.Equal(ModulePrefixes.Version, (int)p.Payload[ModulePrefixes.VersionPayloadKey].IntegerValue);
            Assert.Equal(ModulePrefixes.Derive(Cadena(p, "namespace"), Cadena(p, "relative_path")), Lista(p, ModulePrefixes.PayloadKey));
        }
    }

    [RequiereQdrantLocalFact]
    public async Task DensoFiltraAntesDelCorteConFronterasExactasYCoincideConElOraculo()
    {
        var coleccion = NuevaColeccion();
        var api = Api();
        using var client = api.Cliente(TokenAna);
        try
        {
            await Ingerir(client, coleccion, Corpus);
            var puntos = await Puntos(coleccion);
            PrefijosCorrectos(puntos);
            Assert.Equal(ModulePrefixes.MarcaCompleta, await Marca(coleccion));

            // Los positivos literales de la ficha (namespace exacto, namespace hijo, ruta hija) existen tal cual.
            Assert.Contains(puntos, p => Cadena(p, "namespace") == "alpha");
            Assert.Contains(puntos, p => Cadena(p, "namespace") == "alpha.Foo");
            Assert.Contains(puntos, p => Cadena(p, "relative_path") == "alpha/child/Foo.cs");
            Assert.Contains(puntos, p => Cadena(p, "namespace") == "Company.alpha");
            Assert.Contains(puntos, p => Cadena(p, "namespace") == "Alpha");

            // Sin filtro previo al corte, el top-3 seria de negativos (todos por encima de 0.85).
            var sinFiltro = await Buscar(client, coleccion, 3);
            IgualAlOraculo(Oraculo(puntos, [], 3), sinFiltro);
            Assert.All(sinFiltro, r => Assert.False(Elegible(puntos.Single(p => p.Id.Uuid == r.Id), ["alpha"])));

            var esperado = Oraculo(puntos, ["alpha"], 3);
            Assert.Equal(3, esperado.Count); // hay K elegibles por encima del umbral: la prueba distingue filtrar antes/despues
            var con = await Buscar(client, coleccion, 3, ["alpha"]);
            IgualAlOraculo(esperado, con);
            Assert.All(con, r => Assert.True(Elegible(puntos.Single(p => p.Id.Uuid == r.Id), ["alpha"])));

            // Todos los elegibles con topK holgado: ni negativos (alpha_private, Alpha, Company.alpha, src/alpha,
            // alpha-private/, alphabet/, Alpha/, sin namespace) ni el elegible bajo el umbral.
            var todos = await Buscar(client, coleccion, 100, ["alpha"]);
            IgualAlOraculo(Oraculo(puntos, ["alpha"], 100), todos);
            var rutas = todos.Select(r => Ruta(puntos, r.Id)).ToHashSet();
            Assert.Equal(new HashSet<string> { "x/a1.cs", "x/a2.cs", "alpha/child/Foo.cs", "alpha/p4.md" }, rutas);

            // Mayusculas y espacios no se normalizan.
            var mayus = await Buscar(client, coleccion, 100, ["Alpha"]);
            IgualAlOraculo(Oraculo(puntos, ["Alpha"], 100), mayus);
            Assert.Equal(new HashSet<string> { "y/n2.cs", "Alpha/x.md" }, mayus.Select(r => Ruta(puntos, r.Id)).ToHashSet());
            Assert.Empty(await Buscar(client, coleccion, 100, [" alpha"]));

            // AND por campos distintos (namespace alpha* y ruta x/*), por el mismo campo, e incompatible.
            var distintos = await Buscar(client, coleccion, 100, ["alpha", "x"]);
            IgualAlOraculo(Oraculo(puntos, ["alpha", "x"], 100), distintos);
            Assert.Equal(new HashSet<string> { "x/a1.cs", "x/a2.cs" }, distintos.Select(r => Ruta(puntos, r.Id)).ToHashSet());
            var mismo = await Buscar(client, coleccion, 100, ["alpha", "alpha.Foo"]);
            IgualAlOraculo(Oraculo(puntos, ["alpha", "alpha.Foo"], 100), mismo);
            Assert.Equal(new HashSet<string> { "x/a2.cs" }, mismo.Select(r => Ruta(puntos, r.Id)).ToHashSet());
            Assert.Empty(await Buscar(client, coleccion, 100, ["alpha", "zeta"]));
            Assert.Empty(await Buscar(client, coleccion, 100, ["alpha", "y"]));

            // Con simbolosDefinidos: ANY entre simbolos, AND con el modulo.
            string[] simbolos = ["MA1", "MA3", "MN1", "MN3"];
            var soloSimbolos = await Buscar(client, coleccion, 100, simbolos: simbolos);
            IgualAlOraculo(Oraculo(puntos, [], 100, simbolos: simbolos), soloSimbolos);
            var ambos = await Buscar(client, coleccion, 100, ["alpha"], simbolos);
            var esperadoAmbos = Oraculo(puntos, ["alpha"], 100, simbolos: simbolos);
            Assert.NotEmpty(esperadoAmbos);
            Assert.True(soloSimbolos.Count > esperadoAmbos.Count, "El modulo no acoto nada sobre los simbolos.");
            IgualAlOraculo(esperadoAmbos, ambos);

            // Desempate determinista: repetir da exactamente lo mismo.
            Assert.Equal(todos, await Buscar(client, coleccion, 100, ["alpha"]));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereQdrantLocalFact]
    public async Task RellenoConNamespacesArbitrariosYSeparadoresRepetidosCoincideConElOraculoYEmpatesPorId()
    {
        var coleccion = NuevaColeccion();
        var api = Api();
        using var client = api.Cliente(TokenAna);
        try
        {
            await Ingerir(client, coleccion, [("semilla.md", Md(0.30))]);
            await VolverAntigua(coleccion);

            // Valores que el chunker de C# no produciria, escritos como lo haria un escritor antiguo.
            (string? Ns, string Ruta, float Score)[] crudos =
            [
                ("alpha-private", "y/a.cs", 0.97f), ("alpha..Bar", "y/b.cs", 0.60f), (".alpha", "y/c.cs", 0.96f),
                ("alpha.", "y/d.cs", 0.58f), (null, "alpha//double.md", 0.57f), (null, "/alpha/x.md", 0.95f),
                ("Company.alpha", "alpha", 0.56f), ("", "", 0.94f), (null, "alpha-private/z.md", 0.93f),
                // Empate exacto de score entre elegibles: se ordena por id ordinal.
                ("alpha.T1", "t/1.cs", 0.65f), ("alpha.T2", "t/2.cs", 0.65f), ("alpha.T3", "t/3.cs", 0.65f),
            ];
            foreach (var (ns, ruta, score) in crudos)
                await UpsertAntiguo(coleccion, Guid.NewGuid(), ns, ruta, score);

            var resultado = await Relleno().EjecutarAsync(coleccion, lote: 4, CancellationToken.None);
            Assert.False(resultado.YaCompleta);
            Assert.Equal(crudos.Length + 1, resultado.PuntosActualizados);
            Assert.True(resultado.Paginas >= 3);
            var puntos = await Puntos(coleccion);
            PrefijosCorrectos(puntos);

            foreach (var modulos in new[] { new[] { "alpha" }, ["alpha."], ["alpha/"], ["alpha..Bar"], ["alpha", "alpha.T2"], [".alpha"], ["/alpha"] })
            {
                var esperado = Oraculo(puntos, modulos, 100);
                IgualAlOraculo(esperado, await Buscar(client, coleccion, 100, modulos));
            }

            var empates = await Buscar(client, coleccion, 2, ["alpha"]);
            IgualAlOraculo(Oraculo(puntos, ["alpha"], 2), empates);
            Assert.All(empates, e => Assert.StartsWith("t/", Ruta(puntos, e.Id)));
            Assert.Equal(empates[0].Score, empates[1].Score);
            var conAlpha = await Buscar(client, coleccion, 100, ["alpha"]);
            var rutas = conAlpha.Select(r => Ruta(puntos, r.Id)).ToList();
            Assert.Contains("y/b.cs", rutas);          // alpha..Bar
            Assert.Contains("y/d.cs", rutas);          // alpha.
            Assert.Contains("alpha//double.md", rutas);
            Assert.Contains("alpha", rutas);           // ruta exacta
            Assert.DoesNotContain("y/a.cs", rutas);    // alpha-private
            Assert.DoesNotContain("y/c.cs", rutas);    // .alpha
            Assert.DoesNotContain("/alpha/x.md", rutas);
            Assert.DoesNotContain("alpha-private/z.md", rutas);
            var t = conAlpha.Where(r => Ruta(puntos, r.Id).StartsWith("t/", StringComparison.Ordinal)).Select(r => r.Id).ToList();
            Assert.Equal(t.OrderBy(x => x, StringComparer.Ordinal), t);
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereQdrantLocalFact]
    public async Task FusionNativaFiltraEnAmbasRamasAntesDelCorte()
    {
        var coleccion = NuevaColeccion();
        var api = Api("FusionNativa");
        using var client = api.Cliente(TokenAna);
        try
        {
            await Ingerir(client, coleccion, Corpus);
            var puntos = await Puntos(coleccion);

            foreach (var k in new[] { 1, 3, 100 })
            {
                var esperado = OraculoFusion(puntos, ["alpha"], k);
                Assert.NotEmpty(esperado);
                var real = await Buscar(client, coleccion, k, ["alpha"]);
                Assert.Equal(esperado.Select(e => e.Id), real.Select(r => r.Id));
                Assert.Equal(esperado.Select(e => e.Score), real.Select(r => r.Score));
            }
            // Mismo oraculo sin filtro: el instrumento reproduce la fusion del servicio.
            var sinFiltro = await Buscar(client, coleccion, 3);
            var esperadoSinFiltro = OraculoFusion(puntos, [], 3);
            Assert.Equal(esperadoSinFiltro.Select(e => e.Id), sinFiltro.Select(r => r.Id));
            Assert.Equal(esperadoSinFiltro.Select(e => e.Score), sinFiltro.Select(r => r.Score));
            Assert.All(sinFiltro, r => Assert.False(Elegible(puntos.Single(p => p.Id.Uuid == r.Id), ["alpha"])));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    /// <summary>
    /// Oraculo de la fusion, independiente del indice: sobre los puntos que cumplen el predicado literal, rama densa
    /// (dot con e0, umbral) y rama sparse (dot con la consulta tokenizada, puntos con algun termino comun), cada una
    /// ordenada por score desc e id ordinal y cortada en max(4K, 40); RRF k=2 con rangos desde 0 en f32 (densa y luego
    /// sparse) y corte final en K.
    /// </summary>
    private static List<(string Id, float Score)> OraculoFusion(IReadOnlyList<RetrievedPoint> puntos, IReadOnlyList<string> modulos, int topK)
    {
        var prefetch = Math.Max(topK * 4, 40);
        var elegibles = puntos.Where(p => Elegible(p, modulos)).ToList();
        var consulta = new SparseTokenizer().Tokenize("texto de prueba nota valor modulo")
            .ToDictionary(e => e.TermIndex, e => e.Weight);

        var densa = elegibles
            .Select(p => (Id: p.Id.Uuid, Score: p.Vectors.Vectors.Vectors["dense"].Dense.Data[0]))
            .Where(x => x.Score >= Umbral)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Id, StringComparer.Ordinal).Take(prefetch).ToList();
        var sparse = elegibles
            .Select(p =>
            {
                var v = p.Vectors.Vectors.Vectors[QdrantVectorStore.SparseVectorName].Sparse;
                var comunes = v.Indices.Select((indice, i) => (indice, valor: v.Values[i]))
                    .Where(t => consulta.ContainsKey(t.indice)).OrderBy(t => t.indice).ToList();
                var score = 0f;
                foreach (var (indice, valor) in comunes)
                    score += consulta[indice] * valor;
                return (Id: p.Id.Uuid, Score: score, Comunes: comunes.Count);
            })
            .Where(x => x.Comunes > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Id, StringComparer.Ordinal).Take(prefetch)
            .Select(x => (x.Id, x.Score)).ToList();

        var rrf = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var rama in new[] { densa, sparse })
            for (var rango = 0; rango < rama.Count; rango++)
                rrf[rama[rango].Id] = rrf.GetValueOrDefault(rama[rango].Id) + 1f / (rango + 2);
        return rrf.Select(kv => (Id: kv.Key, Score: kv.Value))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Id, StringComparer.Ordinal).Take(topK).ToList();
    }

    [RequiereQdrantLocalFact]
    public async Task EspacioResumenFiltraAntesDelCorteYCoincideConElOraculo()
    {
        var coleccion = NuevaColeccion();
        var api = Api();
        using var client = api.Cliente(TokenAna);
        try
        {
            await Ingerir(client, coleccion, Corpus, resumen: true);
            var puntos = await Puntos(coleccion);
            Assert.Contains(puntos, p => p.Vectors.Vectors.Vectors.ContainsKey("dense-resumen"));

            var sinFiltro = await Buscar(client, coleccion, 3, espacio: "resumen");
            IgualAlOraculo(Oraculo(puntos, [], 3, "dense-resumen"), sinFiltro);
            var esperado = Oraculo(puntos, ["alpha"], 3, "dense-resumen");
            Assert.Equal(3, esperado.Count);
            IgualAlOraculo(esperado, await Buscar(client, coleccion, 3, ["alpha"], espacio: "resumen"));
            IgualAlOraculo(Oraculo(puntos, ["alpha", "x"], 100, "dense-resumen"),
                await Buscar(client, coleccion, 100, ["alpha", "x"], espacio: "resumen"));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereQdrantLocalFact]
    public async Task ColeccionSinPrepararResponde409AntesDeVectorizarYSinModulosNoCambia()
    {
        var coleccion = NuevaColeccion();
        var api = Api();
        using var client = api.Cliente(TokenAna);
        try
        {
            // Inexistente: 200 vacio tambien con modulos, sin vectorizar.
            var antesInexistente = _contador.Embeddings;
            Assert.Empty(await Buscar(client, coleccion, 10, ["alpha"]));
            Assert.Equal(antesInexistente, _contador.Embeddings);

            await Ingerir(client, coleccion, Corpus, resumen: true);
            var preparada = await Buscar(client, coleccion, 100);
            await VolverAntigua(coleccion);

            // Sin modulos no se exige migracion y el resultado es identico (ids, orden y score).
            Assert.Equal(preparada, await Buscar(client, coleccion, 100));

            async Task Espera409(string? espacio = null)
            {
                var antes = _contador.Embeddings;
                using var r = await PostBusqueda(client, coleccion, 10, ["alpha"], espacio: espacio);
                Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
                Assert.Equal(antes, _contador.Embeddings); // antes de vectorizar
            }

            await Espera409();
            await Espera409("resumen");

            // Marca completa pero sin indices: 409.
            await EscribirMarca(coleccion, ModulePrefixes.MarcaCompleta);
            await Espera409();

            // Indices y marca, pero un punto sin version: 409 (un indice incompleto no da una lista vacia de exito).
            await Relleno().EjecutarAsync(coleccion, 64, CancellationToken.None);
            Assert.NotEmpty(await Buscar(client, coleccion, 10, ["alpha"]));
            var uno = (await Puntos(coleccion)).First();
            await _qdrant.DeletePayloadAsync(coleccion, [ModulePrefixes.VersionPayloadKey], [Guid.Parse(uno.Id.Uuid)], wait: true);
            await Espera409();
            await Espera409("resumen");

            // En curso: 409.
            await Relleno().EjecutarAsync(coleccion, 64, CancellationToken.None);
            await EscribirMarca(coleccion, ModulePrefixes.MarcaEnCurso("otro", DateTimeOffset.UtcNow.AddMinutes(5)));
            await Espera409();

            Assert.Equal(preparada, await Buscar(client, coleccion, 100));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereQdrantLocalFact]
    public async Task RellenoPreservaTodoEsIdempotenteYNoVectorizaNiLlamaAlLlm()
    {
        var coleccion = NuevaColeccion();
        var referencia = NuevaColeccion();
        var api = Api();
        using var client = api.Cliente(TokenAna);
        try
        {
            await Ingerir(client, coleccion, Corpus, resumen: true);
            await Ingerir(client, referencia, Corpus, resumen: true);
            await VolverAntigua(coleccion);

            // Documentos sin cambios: la reingesta los omite y NO repara los puntos antiguos (hace falta el relleno).
            var sinPrepararAntes = await ModulePrefixes.ContarSinPrepararAsync(_qdrant, coleccion, CancellationToken.None);
            var instantaneaAntigua = await Instantanea(coleccion);
            await Ingerir(client, coleccion, Corpus, resumen: true);
            Assert.Equal(sinPrepararAntes, await ModulePrefixes.ContarSinPrepararAsync(_qdrant, coleccion, CancellationToken.None));
            Assert.Equal(instantaneaAntigua.Keys, (await Instantanea(coleccion)).Keys);

            var sinFiltroAntes = await Buscar(client, coleccion, 100);
            var resumenAntes = await Buscar(client, coleccion, 100, espacio: "resumen");
            var antes = await Instantanea(coleccion);
            var (emb, llm) = (_contador.Embeddings, _contador.Llm);

            var r = await Relleno().EjecutarAsync(coleccion, lote: 5, CancellationToken.None);
            Assert.False(r.YaCompleta);
            Assert.Equal((long)antes.Count, r.PuntosActualizados);
            Assert.Equal((emb, llm), (_contador.Embeddings, _contador.Llm));
            Assert.Equal(ModulePrefixes.MarcaCompleta, await Marca(coleccion));

            var despues = await Instantanea(coleccion);
            SoloCambiaronLosCamposDeModulo(antes, despues);
            PrefijosCorrectos(await Puntos(coleccion));

            // Mismos prefijos que escribe el escritor nuevo para los mismos documentos (por ruta: los ids dependen del
            // nombre de la coleccion).
            var puntosDespues = await Puntos(coleccion);
            var puntosReferencia = await Puntos(referencia);
            string Clave(RetrievedPoint p) => Cadena(p, "relative_path") + "#" + p.Payload["start_line"].IntegerValue;
            Assert.Equal(puntosReferencia.Select(Clave).Order(StringComparer.Ordinal), puntosDespues.Select(Clave).Order(StringComparer.Ordinal));
            foreach (var p in puntosDespues)
            {
                var q = puntosReferencia.Single(x => Clave(x) == Clave(p));
                Assert.Equal(q.Payload[ModulePrefixes.PayloadKey].ToString(), p.Payload[ModulePrefixes.PayloadKey].ToString());
                Assert.Equal(q.Payload[ModulePrefixes.VersionPayloadKey].ToString(), p.Payload[ModulePrefixes.VersionPayloadKey].ToString());
            }

            // Sin filtro: mismos ids, orden y scores antes y despues del relleno, en ambos espacios.
            Assert.Equal(sinFiltroAntes, await Buscar(client, coleccion, 100));
            Assert.Equal(resumenAntes, await Buscar(client, coleccion, 100, espacio: "resumen"));

            // Segunda ejecucion: sin efectos (las dos busquedas anteriores si vectorizan la consulta).
            (emb, llm) = (_contador.Embeddings, _contador.Llm);
            var segunda = await Relleno().EjecutarAsync(coleccion, lote: 5, CancellationToken.None);
            Assert.True(segunda.YaCompleta);
            Assert.Equal(0, segunda.PuntosActualizados);
            var tercera = await Instantanea(coleccion);
            Assert.Equal(despues.Keys, tercera.Keys);
            foreach (var id in despues.Keys)
            {
                Assert.Equal(despues[id].Vectores, tercera[id].Vectores);
                Assert.Equal(despues[id].Payload, tercera[id].Payload);
            }
            Assert.Equal((emb, llm), (_contador.Embeddings, _contador.Llm));
            Assert.NotEmpty(await Buscar(client, coleccion, 10, ["alpha"]));
        }
        finally
        {
            await Borrar(coleccion, referencia);
        }
    }

    [RequiereQdrantLocalFact]
    public async Task RellenoInterrumpidoNoMarcaCompletoBloqueaIngestasYSeReanuda()
    {
        var coleccion = NuevaColeccion();
        var api = Api();
        using var client = api.Cliente(TokenAna);
        try
        {
            await Ingerir(client, coleccion, Corpus);
            await VolverAntigua(coleccion);
            var antes = await Instantanea(coleccion);
            var total = (ulong)antes.Count;

            // Error inyectado tras la segunda pagina.
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Relleno((pagina, _) =>
                pagina == 2 ? throw new InvalidOperationException("fallo inyectado") : Task.CompletedTask).EjecutarAsync(coleccion, 3, CancellationToken.None));
            Assert.Equal("fallo inyectado", error.Message);
            var restantes = await ModulePrefixes.ContarSinPrepararAsync(_qdrant, coleccion, CancellationToken.None);
            Assert.Equal(total - 6, restantes);
            var marca = await Marca(coleccion);
            Assert.StartsWith(ModulePrefixes.PrefijoMarcaEnCurso, marca);
            Assert.True(ModulePrefixes.TryParseEnCurso(marca, out _, out var expira) && expira <= DateTimeOffset.UtcNow);

            // Busqueda con modulos: 409, nunca una lista parcial. Ingesta: 409 mientras no se relance.
            using (var b = await PostBusqueda(client, coleccion, 10, ["alpha"]))
                Assert.Equal(HttpStatusCode.Conflict, b.StatusCode);
            using (var i = await PostIngesta(client, coleccion, false, [("nuevo.md", Md(0.70))]))
                Assert.Equal(HttpStatusCode.Conflict, i.StatusCode);

            // Cancelacion: tampoco marca completo.
            using var cts = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Relleno((_, _) =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            }).EjecutarAsync(coleccion, 3, cts.Token));
            Assert.StartsWith(ModulePrefixes.PrefijoMarcaEnCurso, await Marca(coleccion));
            restantes = await ModulePrefixes.ContarSinPrepararAsync(_qdrant, coleccion, CancellationToken.None);
            Assert.Equal(total - 9, restantes);

            // Reanudacion: solo trata lo que faltaba y termina completo.
            var r = await Relleno().EjecutarAsync(coleccion, 3, CancellationToken.None);
            Assert.Equal((long)restantes, r.PuntosActualizados);
            Assert.Equal(ModulePrefixes.MarcaCompleta, await Marca(coleccion));
            SoloCambiaronLosCamposDeModulo(antes, await Instantanea(coleccion));
            PrefijosCorrectos(await Puntos(coleccion));
            Assert.NotEmpty(await Buscar(client, coleccion, 10, ["alpha"]));
            await Ingerir(client, coleccion, [("nuevo.md", Md(0.70))]);
            PrefijosCorrectos(await Puntos(coleccion));
            Assert.Equal(0UL, await ModulePrefixes.ContarSinPrepararAsync(_qdrant, coleccion, CancellationToken.None));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }

    [RequiereQdrantLocalFact]
    public async Task ExclusionDeRellenosYEscritoresAntiguos()
    {
        var coleccion = NuevaColeccion();
        var api = Api();
        using var client = api.Cliente(TokenAna);
        try
        {
            await Ingerir(client, coleccion, Corpus);
            await VolverAntigua(coleccion);
            var total = await ModulePrefixes.ContarSinPrepararAsync(_qdrant, coleccion, CancellationToken.None);

            // Otro relleno con lease vivo: rechazado sin escribir nada.
            var ajena = ModulePrefixes.MarcaEnCurso("otro-run", DateTimeOffset.UtcNow.AddMinutes(5));
            await EscribirMarca(coleccion, ajena);
            var ocupado = await Assert.ThrowsAsync<RellenoModuloException>(() => Relleno().EjecutarAsync(coleccion, 3, CancellationToken.None));
            Assert.Equal(RellenoModuloError.LeaseOcupado, ocupado.Codigo);
            Assert.Equal(ajena, await Marca(coleccion));
            Assert.Equal(total, await ModulePrefixes.ContarSinPrepararAsync(_qdrant, coleccion, CancellationToken.None));

            // Lease expirado de un relleno muerto: se toma.
            await EscribirMarca(coleccion, ModulePrefixes.MarcaEnCurso("muerto", DateTimeOffset.UtcNow.AddMinutes(-1)));

            // Otro proceso toma el lease a mitad: este se detiene sin marcar completo ni pisar la marca ajena.
            var robada = ModulePrefixes.MarcaEnCurso("ladron", DateTimeOffset.UtcNow.AddMinutes(5));
            var perdido = await Assert.ThrowsAsync<RellenoModuloException>(() => Relleno((pagina, ct) =>
                pagina == 1 ? EscribirMarca(coleccion, robada) : Task.CompletedTask).EjecutarAsync(coleccion, 3, CancellationToken.None));
            Assert.Equal(RellenoModuloError.LeasePerdido, perdido.Codigo);
            Assert.Equal(robada, await Marca(coleccion));
            Assert.True(await ModulePrefixes.ContarSinPrepararAsync(_qdrant, coleccion, CancellationToken.None) > 0);

            // Un escritor antiguo que escribe durante el relleno: sus puntos entran en paginas siguientes.
            await EscribirMarca(coleccion, ModulePrefixes.MarcaEnCurso("ladron", DateTimeOffset.UtcNow.AddMinutes(-1)));
            var durante = Guid.NewGuid();
            await Relleno((pagina, _) => pagina == 1
                ? UpsertAntiguo(coleccion, durante, "alpha.Tardio", "tardio/x.cs", 0.99f)
                : Task.CompletedTask).EjecutarAsync(coleccion, 3, CancellationToken.None);
            Assert.Equal(ModulePrefixes.MarcaCompleta, await Marca(coleccion));
            var puntos = await Puntos(coleccion);
            PrefijosCorrectos(puntos);
            Assert.Contains(puntos, p => p.Id.Uuid == durante.ToString());
            Assert.Equal(durante.ToString(), (await Buscar(client, coleccion, 1, ["alpha"]))[0].Id);

            // Escritor antiguo despues de completar: la busqueda con modulos falla cerrada (409), sin modulos sigue.
            var despues = Guid.NewGuid();
            await UpsertAntiguo(coleccion, despues, "alpha", "z.cs", 0.98f);
            using (var b = await PostBusqueda(client, coleccion, 10, ["alpha"]))
                Assert.Equal(HttpStatusCode.Conflict, b.StatusCode);
            Assert.NotEmpty(await Buscar(client, coleccion, 10));

            // Relanzar el relleno lo repara: solo trata ese punto.
            var r = await Relleno().EjecutarAsync(coleccion, 3, CancellationToken.None);
            Assert.Equal(1, r.PuntosActualizados);
            puntos = await Puntos(coleccion);
            IgualAlOraculo(Oraculo(puntos, ["alpha"], 100), await Buscar(client, coleccion, 100, ["alpha"]));
        }
        finally
        {
            await Borrar(coleccion);
        }
    }
}
