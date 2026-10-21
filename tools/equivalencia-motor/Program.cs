// 1.4: equivalencia del motor portado frente al pipeline real de Rag.Context.Engine. Ver README.md.
//
//   ingestar <raiz> <coleccion>                      documentos del escaner real -> POST ingestas (dos pasadas)
//   comparar <coleccion> <coleccion-referencia>      Ids, payload y sparse-code punto a punto
//   vectores <coleccion> <chunks.jsonl> <docs.npy>   Ids/hash contra el banco de 1.11 y coseno del denso
//   buscar <coleccion> <eval-set.json> <resultados-1.11.json> <clave>   POST busquedas con el eval-set; hit@10
//
// Solo contra el Qdrant de docker-compose.motor.yml (127.0.0.1:16334); nunca rag-qdrant (6334).
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using RagEngine.Core.Domain;
using RagEngine.Core.Infrastructure.Scanning;
using Reyma.VectorSpace.Engine.Microservice.Business;

const string QdrantHost = "127.0.0.1";
const int QdrantGrpc = 16334;
var salida = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".cache", "equivalencia-1.4"));
Directory.CreateDirectory(salida);
var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

if (args.Length == 0)
{
    Console.Error.WriteLine("Uso: ingestar|comparar|vectores|buscar ... (ver cabecera de Program.cs)");
    return 2;
}

var qdrant = new QdrantClient(QdrantHost, QdrantGrpc);
switch (args[0])
{
    case "ingestar": return await Ingestar(args[1], args[2]);
    case "comparar": return await Comparar(args[1], args[2]);
    case "vectores": return await Vectores(args[1], args[2], args[3]);
    case "buscar": return await Buscar(args[1], args[2], args[3], args[4]);
    default: Console.Error.WriteLine($"Comando desconocido: {args[0]}"); return 2;
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Ingestar(string raiz, string coleccion)
{
    // Mismo recorrido y misma lectura que DefaultIngestionPipeline: escaner real y File.ReadAllTextAsync.
    var scanner = new FileSystemIngestionScanner(NullLogger<FileSystemIngestionScanner>.Instance);
    var documentos = new List<(string Ruta, string Contenido)>();
    await foreach (var a in scanner.ScanAsync(raiz, ScanProfile.DotNetEnterprise))
        documentos.Add((a.RelativePath, await File.ReadAllTextAsync(a.AbsolutePath)));

    using var servicio = new ServicioEnProceso([coleccion]);
    using var http = servicio.Cliente();

    var informe = new JsonObject { ["coleccion"] = coleccion, ["raiz"] = raiz, ["documentos"] = documentos.Count };
    foreach (var pasada in new[] { "primera", "segunda" })
    {
        var sw = Stopwatch.StartNew();
        int recibidos = 0, indexados = 0, sinCambios = 0, omitidos = 0;
        foreach (var lote in Lotes(documentos))
        {
            var cuerpo = new { origen = new { tipo = "documentos", documentos = lote.Select(d => new { ruta = d.Ruta, contenido = d.Contenido }) } };
            using var respuesta = await http.PostAsync($"/api/colecciones/{coleccion}/ingestas",
                new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json"));
            var texto = await respuesta.Content.ReadAsStringAsync();
            if (!respuesta.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"{(int)respuesta.StatusCode}: {texto}");
                return 1;
            }
            var r = JsonNode.Parse(texto)!;
            recibidos += (int)r["documentosRecibidos"]!; indexados += (int)r["documentosIndexados"]!;
            sinCambios += (int)r["documentosSinCambios"]!; omitidos += (int)r["documentosOmitidos"]!;
            Console.Error.Write('.');
        }
        Console.Error.WriteLine();
        var puntos = await qdrant.CountAsync(coleccion, exact: true);
        informe[pasada] = new JsonObject
        {
            ["segundos"] = Math.Round(sw.Elapsed.TotalSeconds, 1), ["recibidos"] = recibidos, ["indexados"] = indexados,
            ["sinCambios"] = sinCambios, ["omitidos"] = omitidos, ["puntos"] = puntos
        };
        Console.WriteLine($"{coleccion} {pasada}: {informe[pasada]!.ToJsonString()}");
    }
    await File.WriteAllTextAsync(Path.Combine(salida, $"ingesta-{coleccion}.json"), informe.ToJsonString(json));
    return 0;
}

// Solicitudes chicas: el mediador corporativo envuelve cada solicitud en un TransactionScope que expira a los 10 min
// (item 1.4.1), y con TEI en CPU ~64 KB de contenido ya son minutos. Un documento mayor va solo.
static IEnumerable<(string Ruta, string Contenido)[]> Lotes(List<(string Ruta, string Contenido)> documentos)
{
    var lote = new List<(string, string)>();
    var caracteres = 0;
    foreach (var d in documentos)
    {
        if (lote.Count > 0 && (lote.Count == 100 || caracteres + d.Contenido.Length > 64_000))
        {
            yield return lote.ToArray();
            lote.Clear();
            caracteres = 0;
        }
        lote.Add(d);
        caracteres += d.Contenido.Length;
    }
    if (lote.Count > 0) yield return lote.ToArray();
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Comparar(string coleccion, string referencia)
{
    var servicio = await Puntos(coleccion);
    var origen = await Puntos(referencia);

    var soloServicio = servicio.Keys.Except(origen.Keys).ToList();
    var soloOrigen = origen.Keys.Except(servicio.Keys).ToList();
    var comunes = servicio.Keys.Intersect(origen.Keys).ToList();

    // last_modified es la hora de ingesta (o del archivo en el origen); document_fingerprint solo existe aqui.
    var ignoradas = new HashSet<string> { "last_modified", "document_fingerprint" };
    var difPorClave = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var ejemplos = new JsonArray();
    var clavesSoloServicio = new SortedSet<string>(StringComparer.Ordinal);
    var clavesSoloOrigen = new SortedSet<string>(StringComparer.Ordinal);
    int sparseDistintos = 0;

    foreach (var id in comunes)
    {
        var (ps, ss) = servicio[id];
        var (po, so) = origen[id];
        foreach (var k in ps.Keys.Except(po.Keys)) clavesSoloServicio.Add(k);
        foreach (var k in po.Keys.Except(ps.Keys)) clavesSoloOrigen.Add(k);
        foreach (var k in ps.Keys.Intersect(po.Keys).Where(k => !ignoradas.Contains(k)))
        {
            if (ps[k] == po[k]) continue;
            difPorClave[k] = difPorClave.GetValueOrDefault(k) + 1;
            if (ejemplos.Count < 5)
                ejemplos.Add(new JsonObject { ["id"] = id, ["clave"] = k, ["servicio"] = Recortar(ps[k]), ["origen"] = Recortar(po[k]) });
        }
        if (ss != so) sparseDistintos++;
    }

    var informe = new JsonObject
    {
        ["coleccion"] = coleccion, ["referencia"] = referencia,
        ["puntos_servicio"] = servicio.Count, ["puntos_referencia"] = origen.Count, ["ids_comunes"] = comunes.Count,
        ["ids_solo_servicio"] = soloServicio.Count, ["ids_solo_referencia"] = soloOrigen.Count,
        ["claves_payload_solo_servicio"] = new JsonArray(clavesSoloServicio.Select(k => (JsonNode)k).ToArray()),
        ["claves_payload_solo_referencia"] = new JsonArray(clavesSoloOrigen.Select(k => (JsonNode)k).ToArray()),
        ["payload_distinto_por_clave"] = new JsonObject(difPorClave.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
        ["sparse_code_distintos"] = sparseDistintos,
        ["ejemplos"] = ejemplos
    };
    Console.WriteLine(informe.ToJsonString(json));
    await File.WriteAllTextAsync(Path.Combine(salida, $"comparar-{coleccion}.json"), informe.ToJsonString(json));
    return soloServicio.Count == 0 && soloOrigen.Count == 0 && difPorClave.Count == 0 && sparseDistintos == 0 ? 0 : 3;
}

static string Recortar(string s) => s.Length <= 300 ? s : s[..300] + "...";

async Task<Dictionary<string, (Dictionary<string, string> Payload, string Sparse)>> Puntos(string coleccion)
{
    var puntos = new Dictionary<string, (Dictionary<string, string>, string)>(StringComparer.Ordinal);
    PointId? offset = null;
    while (true)
    {
        var r = await qdrant.ScrollAsync(coleccion, limit: 512, offset: offset,
            payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: new WithVectorsSelector { Include = new VectorsSelector { Names = { "sparse-code" } } });
        foreach (var p in r.Result)
        {
            var sv = p.Vectors.Vectors.Vectors["sparse-code"].Sparse;
            var sparse = string.Join(',', sv.Indices.Zip(sv.Values, (i, v) => $"{i}:{v:R}"));
            puntos[Guid.Parse(p.Id.Uuid).ToString("D")] = (p.Payload.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()), sparse);
        }
        if (r.NextPageOffset is null || r.NextPageOffset.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.None) break;
        offset = r.NextPageOffset;
    }
    return puntos;
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Vectores(string coleccion, string chunksJsonl, string docsNpy)
{
    var cache = File.ReadLines(chunksJsonl).Select(l => JsonNode.Parse(l)!).ToList();
    var (filas, columnas, datos) = LeerNpy(docsNpy);
    if (filas != cache.Count) throw new InvalidOperationException($"{docsNpy}: {filas} filas para {cache.Count} chunks");

    var servicio = new Dictionary<string, (string Hash, float[] Denso)>(StringComparer.Ordinal);
    PointId? offset = null;
    while (true)
    {
        var r = await qdrant.ScrollAsync(coleccion, limit: 512, offset: offset,
            payloadSelector: new WithPayloadSelector { Include = new PayloadIncludeSelector { Fields = { "content_hash" } } },
            vectorsSelector: new WithVectorsSelector { Include = new VectorsSelector { Names = { "dense" } } });
        foreach (var p in r.Result)
            servicio[Guid.Parse(p.Id.Uuid).ToString("D")] =
                (p.Payload["content_hash"].StringValue, p.Vectors.Vectors.Vectors["dense"].Dense.Data.ToArray());
        if (r.NextPageOffset is null || r.NextPageOffset.PointIdOptionsCase == PointId.PointIdOptionsOneofCase.None) break;
        offset = r.NextPageOffset;
    }

    var dim = servicio.Values.First().Denso.Length;
    var cosenos = new List<(double Cos, string Id)>();
    int faltan = 0, hashDistinto = 0;
    for (var i = 0; i < cache.Count; i++)
    {
        var id = (string)cache[i]!["id"]!;
        if (!servicio.TryGetValue(id, out var s)) { faltan++; continue; }
        if (s.Hash != (string)cache[i]!["content_hash"]!) hashDistinto++;
        // Banco de 1.11: el vector completo (768) truncado a la dimension servida y normalizado L2.
        var referencia = new double[dim];
        double norma = 0;
        for (var j = 0; j < dim; j++) { referencia[j] = datos[(long)i * columnas + j]; norma += referencia[j] * referencia[j]; }
        norma = Math.Sqrt(norma);
        double cos = 0;
        for (var j = 0; j < dim; j++) cos += s.Denso[j] * referencia[j] / norma;
        cosenos.Add((cos, id));
    }
    var orden = cosenos.OrderBy(c => c.Cos).ToList();
    double Percentil(double p) => orden[(int)Math.Min(orden.Count - 1, Math.Floor(p * orden.Count))].Cos;
    var informe = new JsonObject
    {
        ["coleccion"] = coleccion, ["banco_chunks"] = cache.Count, ["puntos_servicio"] = servicio.Count,
        ["ids_del_banco_ausentes"] = faltan, ["ids_extra_en_servicio"] = servicio.Count - (cache.Count - faltan),
        ["content_hash_distinto"] = hashDistinto, ["dimension"] = dim,
        ["coseno_min"] = Math.Round(orden[0].Cos, 6), ["coseno_p01"] = Math.Round(Percentil(0.01), 6),
        ["coseno_p50"] = Math.Round(Percentil(0.5), 6), ["coseno_lt_0_999"] = orden.Count(c => c.Cos < 0.999),
        ["peores"] = new JsonArray(orden.Take(5).Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["cos"] = Math.Round(c.Cos, 5) }).ToArray())
    };
    Console.WriteLine(informe.ToJsonString(json));
    await File.WriteAllTextAsync(Path.Combine(salida, $"vectores-{coleccion}.json"), informe.ToJsonString(json));
    return 0;
}

// .npy v1/v2/v3, float32 little-endian, C-order, 2 dimensiones: lo que escribe numpy.save en el banco.
static (int Filas, int Columnas, float[] Datos) LeerNpy(string ruta)
{
    var bytes = File.ReadAllBytes(ruta);
    var version = bytes[6];
    var largoCabecera = version == 1 ? BitConverter.ToUInt16(bytes, 8) : (int)BitConverter.ToUInt32(bytes, 8);
    var inicio = version == 1 ? 10 : 12;
    var cabecera = Encoding.ASCII.GetString(bytes, inicio, largoCabecera);
    if (!cabecera.Contains("'<f4'") || cabecera.Contains("'fortran_order': True"))
        throw new InvalidOperationException($"{ruta}: se esperaba float32 C-order ({cabecera})");
    var forma = cabecera[(cabecera.IndexOf("'shape': (", StringComparison.Ordinal) + 10)..];
    var dims = forma[..forma.IndexOf(')')].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
    var datos = new float[dims[0] * dims[1]];
    Buffer.BlockCopy(bytes, inicio + largoCabecera, datos, 0, datos.Length * 4);
    return (dims[0], dims[1], datos);
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Buscar(string coleccion, string evalSet, string resultados111, string clave)
{
    var items = JsonNode.Parse(await File.ReadAllTextAsync(evalSet))!.AsArray();
    var previo = JsonNode.Parse(await File.ReadAllTextAsync(resultados111))!["colecciones"]![clave]!["denso-es"]!["preguntas"]!.AsArray()
        .ToDictionary(p => (string)p!["question"]!, p => p!);

    using var servicio = new ServicioEnProceso([coleccion]);
    using var http = servicio.Cliente();

    var filas = new JsonArray();
    int respondibles = 0, aciertos = 0, aciertosPrevio = 0, mismoTop10 = 0, coincidenAcierto = 0;
    double solapeTotal = 0;
    foreach (var item in items)
    {
        var pregunta = (string)item!["Question"]!;
        var cuerpo = JsonSerializer.Serialize(new { consulta = pregunta, topK = 10 });
        using var respuesta = await http.PostAsync($"/api/colecciones/{coleccion}/busquedas", new StringContent(cuerpo, Encoding.UTF8, "application/json"));
        var texto = await respuesta.Content.ReadAsStringAsync();
        if (!respuesta.IsSuccessStatusCode) { Console.Error.WriteLine($"{(int)respuesta.StatusCode}: {texto}"); return 1; }
        var resultados = JsonNode.Parse(texto)!["resultados"]!.AsArray();

        // Regla de harness.evaluar_item (EvalCommand.Evaluate): archivo objetivo por nombre y ancla por subcadena.
        var objetivos = (item["SourceFiles"]?.AsArray().Select(x => (string)x!) ??
                         (item["SourceFile"] is { } sf && (string?)sf is { } s ? [s] : [])).Select(x => x.ToLowerInvariant()).ToHashSet();
        var anclas = item["TargetContentContains"]?.AsArray().Select(x => (string)x!).ToList() ?? [];
        var ids = resultados.Select(r => (string)r!["id"]!).ToList();
        bool? acierto = objetivos.Count == 0 || anclas.Count == 0 ? null : resultados.Any(r =>
            objetivos.Contains(Path.GetFileName(((string)r!["ruta"]!).Replace('\\', '/')).ToLowerInvariant()) &&
            anclas.Any(a => ((string)r["texto"]!).Contains(a, StringComparison.Ordinal)));

        var p = previo[pregunta];
        var idsPrevio = p["top_ids"]!.AsArray().Select(x => (string)x!).ToList();
        var aciertoPrevio = (bool?)p["hit_any_at_k"]?["10"];
        var solape = ids.Intersect(idsPrevio).Count();
        if (acierto is not null)
        {
            respondibles++;
            if (acierto.Value) aciertos++;
            if (aciertoPrevio == true) aciertosPrevio++;
            if (acierto == aciertoPrevio) coincidenAcierto++;
        }
        if (ids.SequenceEqual(idsPrevio)) mismoTop10++;
        solapeTotal += solape;
        filas.Add(new JsonObject
        {
            ["pregunta"] = pregunta, ["hit10"] = acierto, ["hit10_1_11"] = aciertoPrevio, ["solape_top10"] = solape,
            ["mismo_orden"] = ids.SequenceEqual(idsPrevio), ["top_ids"] = new JsonArray(ids.Select(i => (JsonNode)i).ToArray())
        });
    }

    var informe = new JsonObject
    {
        ["coleccion"] = coleccion, ["preguntas"] = items.Count, ["respondibles"] = respondibles,
        ["hit10_servicio"] = aciertos, ["hit10_1_11_c1024_w8192_denso_es"] = aciertosPrevio,
        ["mismo_acierto_por_pregunta"] = coincidenAcierto, ["top10_identico_en_orden"] = mismoTop10,
        ["solape_top10_medio"] = Math.Round(solapeTotal / items.Count, 2), ["detalle"] = filas
    };
    var resumen = informe.DeepClone().AsObject();
    resumen.Remove("detalle");
    Console.WriteLine(resumen.ToJsonString(json));
    await File.WriteAllTextAsync(Path.Combine(salida, $"buscar-{coleccion}.json"), informe.ToJsonString(json));
    return 0;
}

// ---------------------------------------------------------------------------------------------------------------

/// <summary>
/// La API del servicio en proceso, como en las pruebas (ApiFactory/IdentidadDePrueba): sin Seq ni configuracion local,
/// emisor JWT de prueba y una concesion para el usuario "equivalencia" sobre las colecciones dadas. Motor real.
/// </summary>
sealed class ServicioEnProceso(IReadOnlyList<string> colecciones) : WebApplicationFactory<Reyma.VectorSpace.Engine.Microservice.Api.Program>
{
    private const string Emisor = "https://equivalencia.prueba.invalid";
    private static readonly SymmetricSecurityKey Clave = new(RandomNumberGenerator.GetBytes(32));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Fuera de tests/ la factory busca la API junto a la .sln; vive en src/.
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Reyma.VectorSpace.Engine.Microservice.Api")));
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.Sources.Clear();
            config.AddJsonFile("appsettings.json", optional: false);
            var valores = new Dictionary<string, string?>
            {
                ["ASPNETCORE_BASEPATH"] = "", ["AllowedOrigins"] = "",
                ["IdentityServerAuthenticationOptions:Authority"] = "", ["PermissionsValidatorOptions:ApiUri"] = "",
                ["IntegrityServiceOptions:ApiUri"] = "",
                ["Serilog:MinimumLevel:Default"] = "Fatal", ["Serilog:MinimumLevel:Override:Microsoft"] = "Fatal",
                ["Serilog:MinimumLevel:Override:System"] = "Fatal",
                ["Motor:Qdrant:Host"] = "127.0.0.1", ["Motor:Qdrant:PuertoGrpc"] = "16334",
                ["Motor:Embeddings:UrlBase"] = "http://127.0.0.1:18080",
            };
            for (var i = 0; i < colecciones.Count; i++)
            {
                valores[$"ControlDeAcceso:Concesiones:{i}:Coleccion"] = colecciones[i];
                valores[$"ControlDeAcceso:Concesiones:{i}:Operaciones:0"] = "Consultar";
                valores[$"ControlDeAcceso:Concesiones:{i}:Operaciones:1"] = "Ingestar";
                valores[$"ControlDeAcceso:Concesiones:{i}:Usuarios:0"] = "equivalencia";
            }
            config.AddInMemoryCollection(valores);
        });
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.TokenValidationParameters.ValidIssuer = Emisor;
                o.TokenValidationParameters.IssuerSigningKey = Clave;
            }));
    }

    public HttpClient Cliente()
    {
        var client = CreateClient();
        client.Timeout = TimeSpan.FromHours(2);
        var fin = DateTime.UtcNow.AddHours(4);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emisor, Audience = ServicioInfo.NombreApi,
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["preferred_username"] = "equivalencia" },
            NotBefore = DateTime.UtcNow.AddMinutes(-1), IssuedAt = DateTime.UtcNow.AddMinutes(-1), Expires = fin,
            SigningCredentials = new SigningCredentials(Clave, SecurityAlgorithms.HmacSha256)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
