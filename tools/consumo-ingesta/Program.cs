// 1.7: modelo de consumo de ingesta. Ver README.md.
//
//   corpus <raiz>                               tamano del repo y coste de escanearlo y leerlo en local (lo que haria 'ruta')
//   transferencia <raiz> <lote-corte> <seg>     repo completo por POST ingestas en lotes de 100, corte y reintento de un lote,
//                                               segunda pasada "sin cambios"; bytes, tiempo y memoria por lote
//   cuerpo                                      limite efectivo del cuerpo HTTP (documentos .bin: fuera de perfil, no vectoriza)
//   concurrencia <raiz> [caracteres]            una ingesta vs dos simultaneas (TEI compartido), latencia de busquedas y
//                                               embeddings mientras tanto, y el mismo disparo dos veces a la vez
//   saturacion <raiz> <n>                       n ingestas simultaneas con el limite por defecto: cuantas 200 y cuantas 429
//   limpiar                                     borra las colecciones que esta herramienta creo (y solo esas)
//
// Destino: VSE_QDRANT_HOST / VSE_QDRANT_GRPC / VSE_TEI_URL (por defecto 127.0.0.1, 6334, http://127.0.0.1:8081: el tunel
// SSH al stack de corpreyiaprod de 1.7). Solo crea y borra colecciones it-1-7-*; no lista ni toca ninguna otra.
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net;
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
using RagEngine.Core.Domain;
using RagEngine.Core.Infrastructure.Scanning;
using Reyma.VectorSpace.Engine.Microservice.Business;

const string Prefijo = "it-1-7-";
var qdrantHost = Environment.GetEnvironmentVariable("VSE_QDRANT_HOST") ?? "127.0.0.1";
var qdrantGrpc = int.Parse(Environment.GetEnvironmentVariable("VSE_QDRANT_GRPC") ?? "6334");
var teiUrl = Environment.GetEnvironmentVariable("VSE_TEI_URL") ?? "http://127.0.0.1:8081";

var salida = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".cache", "consumo-1.7"));
Directory.CreateDirectory(salida);
var registroCreadas = Path.Combine(salida, "creadas.txt");
var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

if (args.Length == 0)
{
    Console.Error.WriteLine("Uso: corpus|transferencia|cuerpo|concurrencia|saturacion|limpiar ... (ver cabecera de Program.cs)");
    return 2;
}

var qdrant = new QdrantClient(qdrantHost, qdrantGrpc);
var sockets = new ContadorSockets();
switch (args[0])
{
    case "corpus": return await Corpus(args[1]);
    case "transferencia": return await Transferencia(args[1], int.Parse(args[2]), double.Parse(args[3]));
    case "cuerpo": return await Cuerpo();
    case "concurrencia": return await Concurrencia(args[1], args.Length > 2 ? int.Parse(args[2]) : 40_000);
    case "saturacion": return await Saturacion(args[1], int.Parse(args[2]));
    case "limpiar": return await Limpiar();
    default: Console.Error.WriteLine($"Comando desconocido: {args[0]}"); return 2;
}

// ---------------------------------------------------------------------------------------------------------------

async Task<List<(string Ruta, string Contenido)>> Escanear(string raiz)
{
    // Mismo recorrido y misma lectura que DefaultIngestionPipeline: escaner real y File.ReadAllTextAsync.
    var scanner = new FileSystemIngestionScanner(NullLogger<FileSystemIngestionScanner>.Instance);
    var documentos = new List<(string Ruta, string Contenido)>();
    await foreach (var a in scanner.ScanAsync(raiz, ScanProfile.DotNetEnterprise))
        documentos.Add((a.RelativePath, await File.ReadAllTextAsync(a.AbsolutePath)));
    return documentos;
}

static string CuerpoIngesta(IEnumerable<(string Ruta, string Contenido)> lote) =>
    JsonSerializer.Serialize(new { origen = new { tipo = "documentos", documentos = lote.Select(d => new { ruta = d.Ruta, contenido = d.Contenido }) } });

// El maximo del contrato (Limites.MaxDocumentosPorIngesta): un repo entero no cabe en una solicitud.
static List<(string Ruta, string Contenido)[]> Lotes(List<(string Ruta, string Contenido)> documentos) =>
    documentos.Chunk(100).ToList();

async Task Registrar(string coleccion)
{
    if (!coleccion.StartsWith(Prefijo, StringComparison.Ordinal)) throw new InvalidOperationException(coleccion);
    var ya = File.Exists(registroCreadas) ? File.ReadAllLines(registroCreadas) : [];
    if (!ya.Contains(coleccion)) await File.AppendAllLinesAsync(registroCreadas, [coleccion]);
    if (await qdrant.CollectionExistsAsync(coleccion))
        throw new InvalidOperationException($"{coleccion} ya existe: correr 'limpiar' antes de medir desde cero.");
}

async Task<(int Status, JsonNode? Cuerpo, double Segundos, string? RetryAfter)> Post(HttpClient http, string ruta, string cuerpo, CancellationToken ct = default)
{
    var sw = Stopwatch.StartNew();
    using var respuesta = await http.PostAsync(ruta, new StringContent(cuerpo, Encoding.UTF8, "application/json"), ct);
    var texto = await respuesta.Content.ReadAsStringAsync(ct);
    sw.Stop();
    JsonNode? nodo = null;
    try { nodo = string.IsNullOrWhiteSpace(texto) ? null : JsonNode.Parse(texto); } catch (JsonException) { nodo = texto; }
    return ((int)respuesta.StatusCode, nodo, Math.Round(sw.Elapsed.TotalSeconds, 2),
        respuesta.Headers.RetryAfter?.Delta?.TotalSeconds.ToString() ?? respuesta.Headers.RetryAfter?.ToString());
}

static long BytesUtf8(string s) => Encoding.UTF8.GetByteCount(s);
static double Mb(long bytes) => Math.Round(bytes / 1024d / 1024d, 2);

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Corpus(string raiz)
{
    var sw = Stopwatch.StartNew();
    var docs = await Escanear(raiz);
    var segundos = sw.Elapsed.TotalSeconds;
    sw.Restart();
    var cuerpos = Lotes(docs).Select(CuerpoIngesta).ToList();
    var serializar = sw.Elapsed.TotalSeconds;
    var informe = new JsonObject
    {
        ["raiz"] = raiz, ["documentos"] = docs.Count,
        ["contenido_mb_utf8"] = Mb(docs.Sum(d => BytesUtf8(d.Contenido))),
        ["documento_mayor_kb"] = Math.Round(docs.Max(d => BytesUtf8(d.Contenido)) / 1024d, 1),
        ["escanear_y_leer_segundos"] = Math.Round(segundos, 3),
        ["solicitudes_de_100"] = cuerpos.Count,
        ["cuerpos_json_mb"] = Mb(cuerpos.Sum(BytesUtf8)),
        ["cuerpo_json_mayor_mb"] = Mb(cuerpos.Max(BytesUtf8)),
        ["serializar_segundos"] = Math.Round(serializar, 3),
        ["por_extension"] = new JsonObject(docs.GroupBy(d => Path.GetExtension(d.Ruta).ToLowerInvariant())
            .OrderByDescending(g => g.Count()).Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count())))
    };
    Console.WriteLine(informe.ToJsonString(json));
    await File.WriteAllTextAsync(Path.Combine(salida, "corpus.json"), informe.ToJsonString(json));
    return 0;
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Transferencia(string raiz, int loteCorte, double segundosCorte)
{
    const string coleccion = Prefijo + "micro-repo";
    var docs = await Escanear(raiz);
    var lotes = Lotes(docs);
    await Registrar(coleccion);

    using var servicio = new ServicioEnProceso(qdrantHost, qdrantGrpc, teiUrl);
    using var http = servicio.Cliente();
    var ruta = $"/api/colecciones/{coleccion}/ingestas";
    var informe = new JsonObject
    {
        ["coleccion"] = coleccion, ["raiz"] = raiz, ["documentos"] = docs.Count, ["solicitudes"] = lotes.Count,
        ["contenido_mb_utf8"] = Mb(docs.Sum(d => BytesUtf8(d.Contenido))),
        ["destino"] = new JsonObject { ["qdrant_grpc"] = $"{qdrantHost}:{qdrantGrpc}", ["tei"] = teiUrl }
    };

    foreach (var pasada in new[] { "primera", "segunda" })
    {
        var (enviados0, recibidos0) = await sockets.Leer();
        using var memoria = new Muestreo();
        var total = Stopwatch.StartNew();
        var filas = new JsonArray();
        long bytesCuerpos = 0, bytesRespuestas = 0;
        int indexados = 0, sinCambios = 0, omitidos = 0;
        for (var i = 0; i < lotes.Count; i++)
        {
            var cuerpo = CuerpoIngesta(lotes[i]);
            var bytes = BytesUtf8(cuerpo);
            var fila = new JsonObject { ["lote"] = i, ["documentos"] = lotes[i].Length, ["cuerpo_mb"] = Mb(bytes) };

            if (pasada == "primera" && i == loteCorte)
            {
                // Reintento: el cliente corta la solicitud a medias (timeout, red caida) y la repite entera.
                var antes = await qdrant.CollectionExistsAsync(coleccion) ? await qdrant.CountAsync(coleccion, exact: true) : 0UL;
                var corte = Stopwatch.StartNew();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(segundosCorte));
                string desenlace;
                try { var r0 = await Post(http, ruta, cuerpo, cts.Token); desenlace = $"termino antes del corte ({r0.Status})"; }
                catch (OperationCanceledException) { desenlace = "cortada por el cliente"; }
                bytesCuerpos += bytes;
                await Task.Delay(TimeSpan.FromSeconds(3)); // que el servidor note la cancelacion y suelte la coleccion
                var tras = await qdrant.CountAsync(coleccion, exact: true);
                fila["corte"] = new JsonObject
                {
                    ["segundos"] = Math.Round(corte.Elapsed.TotalSeconds, 1), ["desenlace"] = desenlace,
                    ["puntos_nuevos_que_quedaron"] = (long)(tras - antes)
                };
            }

            var (status, r, segundos, _) = await Post(http, ruta, cuerpo);
            bytesCuerpos += bytes;
            if (status != 200)
            {
                Console.Error.WriteLine($"lote {i}: {status} {r?.ToJsonString()}");
                return 1;
            }
            bytesRespuestas += BytesUtf8(r!.ToJsonString());
            indexados += (int)r["documentosIndexados"]!; sinCambios += (int)r["documentosSinCambios"]!; omitidos += (int)r["documentosOmitidos"]!;
            fila["segundos"] = segundos;
            fila["indexados"] = (int)r["documentosIndexados"]!; fila["sin_cambios"] = (int)r["documentosSinCambios"]!;
            fila["omitidos"] = (int)r["documentosOmitidos"]!;
            fila["working_set_mb"] = Mb(Environment.WorkingSet);
            filas.Add(fila);
            Console.Error.WriteLine($"{pasada} lote {i}: {fila.ToJsonString()}");
        }
        total.Stop();
        var (enviados1, recibidos1) = await sockets.Leer();
        var puntos = await qdrant.CountAsync(coleccion, exact: true);
        // Los sockets cuentan las dos puntas del loopback cliente<->Kestrel (una vez cada sentido en enviados y en
        // recibidos) mas el trafico al tunel (Qdrant gRPC + TEI). Restando el loopback queda lo que cruzo el tunel.
        var loopback = bytesCuerpos + bytesRespuestas;
        informe[pasada] = new JsonObject
        {
            ["segundos"] = Math.Round(total.Elapsed.TotalSeconds, 1), ["indexados"] = indexados, ["sin_cambios"] = sinCambios,
            ["omitidos"] = omitidos, ["puntos"] = puntos, ["cuerpos_mb"] = Mb(bytesCuerpos),
            ["tunel_mb_enviados_aprox"] = Mb(enviados1 - enviados0 - loopback),
            ["tunel_mb_recibidos_aprox"] = Mb(recibidos1 - recibidos0 - loopback),
            ["working_set_max_mb"] = Mb(memoria.Maximo), ["gc_heap_mb"] = Mb(GC.GetGCMemoryInfo().HeapSizeBytes),
            ["lotes"] = filas
        };
        Console.WriteLine($"{pasada}: {Resumen(informe[pasada]!)}");
    }
    await File.WriteAllTextAsync(Path.Combine(salida, "transferencia.json"), informe.ToJsonString(json));
    return 0;
}

static string Resumen(JsonNode n)
{
    var c = n.DeepClone().AsObject();
    c.Remove("lotes"); c.Remove("detalle");
    return c.ToJsonString();
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Cuerpo()
{
    const string coleccion = Prefijo + "cuerpo";
    await Registrar(coleccion);
    using var servicio = new ServicioEnProceso(qdrantHost, qdrantGrpc, teiUrl);
    using var http = servicio.Cliente();
    var filas = new JsonArray();
    // 400 000 caracteres ASCII por documento (bajo el 1 MiB del contrato); .bin queda fuera del perfil y no se vectoriza.
    var contenido = new string('a', 400_000);
    foreach (var n in new[] { 20, 70, 100 })
    {
        var cuerpo = CuerpoIngesta(Enumerable.Range(0, n).Select(i => ($"grande/{i}.bin", contenido)));
        using var memoria = new Muestreo();
        var (status, r, segundos, _) = await Post(http, $"/api/colecciones/{coleccion}/ingestas", cuerpo);
        var fila = new JsonObject
        {
            ["documentos"] = n, ["cuerpo_mb"] = Mb(BytesUtf8(cuerpo)), ["status"] = status, ["segundos"] = segundos,
            ["working_set_max_mb"] = Mb(memoria.Maximo),
            ["respuesta"] = r is JsonObject o ? Recortar(o) : r?.ToJsonString()
        };
        filas.Add(fila);
        Console.WriteLine(fila.ToJsonString());
    }
    await File.WriteAllTextAsync(Path.Combine(salida, "cuerpo.json"), new JsonObject { ["coleccion"] = coleccion, ["pruebas"] = filas }.ToJsonString(json));
    return 0;
}

static string Recortar(JsonObject o)
{
    var s = o.ToJsonString();
    return s.Length <= 400 ? s : s[..400] + "...";
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Concurrencia(string raiz, int caracteres)
{
    var docs = await Escanear(raiz);
    // Dos subconjuntos disjuntos de ~<caracteres>, con extensiones del perfil.
    var (a, resto) = Tomar(docs, caracteres);
    var (b, _) = Tomar(resto, caracteres);
    string[] nombres = ["sec-a", "sec-b", "con-a", "con-b", "dup"];
    foreach (var n in nombres)
    {
        // Repetible: estas cinco son solo de esta medicion (it-1-7-*, ya en el registro de creadas).
        if (await qdrant.CollectionExistsAsync(Prefijo + n)) await qdrant.DeleteCollectionAsync(Prefijo + n);
        await Registrar(Prefijo + n);
    }

    // Limite holgado a proposito: aqui se mide que hace el TEI compartido con dos ingestas a la vez, no el limite.
    using var servicio = new ServicioEnProceso(qdrantHost, qdrantGrpc, teiUrl, new()
    {
        ["LimiteDeIngestas:MaximoSimultaneas"] = "8", ["LimiteDeIngestas:MaximoEnCola"] = "8"
    });
    using var http = servicio.Cliente();
    const string consultada = Prefijo + "micro-repo"; // la dejo 'transferencia'; solo se lee
    if (!await qdrant.CollectionExistsAsync(consultada)) { Console.Error.WriteLine("Falta correr 'transferencia' antes."); return 1; }

    async Task<JsonObject> Latencias(Func<bool> seguir, int maximo)
    {
        var busquedas = new List<double>(); var embeddings = new List<double>();
        do
        {
            var q = await Post(http, $"/api/colecciones/{consultada}/busquedas", JsonSerializer.Serialize(new { consulta = "como se crea un ticket", topK = 10 }));
            if (q.Status == 200) busquedas.Add(q.Segundos);
            var e = await Post(http, $"/api/colecciones/{consultada}/embeddings", JsonSerializer.Serialize(new { textos = new[] { "texto suelto de prueba" } }));
            if (e.Status == 200) embeddings.Add(e.Segundos);
            await Task.Delay(1000);
        } while (seguir() && busquedas.Count < maximo);
        return new JsonObject
        {
            ["muestras"] = busquedas.Count,
            ["busqueda_mediana_s"] = Mediana(busquedas), ["busqueda_max_s"] = busquedas.Count == 0 ? null : busquedas.Max(),
            ["embeddings_mediana_s"] = Mediana(embeddings), ["embeddings_max_s"] = embeddings.Count == 0 ? null : embeddings.Max()
        };
    }

    async Task<JsonObject> Ingerir(string coleccion, (string, string)[] lote)
    {
        var (status, r, segundos, _) = await Post(http, $"/api/colecciones/{coleccion}/ingestas", CuerpoIngesta(lote));
        var puntos = await qdrant.CollectionExistsAsync(coleccion) ? (long)await qdrant.CountAsync(coleccion, exact: true) : 0;
        return new JsonObject
        {
            ["coleccion"] = coleccion, ["status"] = status, ["segundos"] = segundos, ["puntos"] = puntos,
            ["indexados"] = r?["documentosIndexados"]?.GetValue<int>(), ["sin_cambios"] = r?["documentosSinCambios"]?.GetValue<int>(),
            ["chunks_por_s"] = segundos > 0 ? Math.Round(puntos / segundos, 2) : null
        };
    }

    var informe = new JsonObject { ["documentos_a"] = a.Length, ["documentos_b"] = b.Length };
    informe["latencia_en_reposo"] = await Latencias(() => true, 5);
    Console.WriteLine($"reposo: {informe["latencia_en_reposo"]!.ToJsonString()}");

    // Una ingesta a la vez (A y despues B), midiendo latencia mientras corre A.
    var total = Stopwatch.StartNew();
    var tareaA = Ingerir(Prefijo + "sec-a", a);
    var latUna = await Latencias(() => !tareaA.IsCompleted, 1000);
    var secA = await tareaA;
    var secB = await Ingerir(Prefijo + "sec-b", b);
    informe["secuencial"] = new JsonObject
    {
        ["a"] = secA, ["b"] = secB, ["segundos_total"] = Math.Round(total.Elapsed.TotalSeconds, 1), ["latencia_con_una_ingesta"] = latUna
    };
    Console.WriteLine($"secuencial: {Resumen(informe["secuencial"]!)}");

    // Las dos a la vez en colecciones nuevas (mismo contenido que A y B).
    total.Restart();
    var conA = Ingerir(Prefijo + "con-a", a);
    var conB = Ingerir(Prefijo + "con-b", b);
    var ambas = Task.WhenAll(conA, conB);
    var latDos = await Latencias(() => !ambas.IsCompleted, 1000);
    await ambas;
    informe["simultaneo"] = new JsonObject
    {
        ["a"] = conA.Result, ["b"] = conB.Result, ["segundos_total"] = Math.Round(total.Elapsed.TotalSeconds, 1), ["latencia_con_dos_ingestas"] = latDos
    };
    Console.WriteLine($"simultaneo: {Resumen(informe["simultaneo"]!)}");

    // Mismo disparo dos veces a la vez (p. ej. dos pushes seguidos, o un webhook reintentado) sobre la misma coleccion.
    var d1 = Post(http, $"/api/colecciones/{Prefijo}dup/ingestas", CuerpoIngesta(a));
    var d2 = Post(http, $"/api/colecciones/{Prefijo}dup/ingestas", CuerpoIngesta(a));
    await Task.WhenAll(d1, d2);
    informe["disparo_duplicado"] = new JsonObject
    {
        ["respuestas"] = new JsonArray(new[] { d1.Result, d2.Result }.Select(x => (JsonNode)new JsonObject
        {
            ["status"] = x.Status, ["segundos"] = x.Segundos,
            ["indexados"] = x.Cuerpo?["documentosIndexados"]?.GetValue<int>(), ["sin_cambios"] = x.Cuerpo?["documentosSinCambios"]?.GetValue<int>()
        }).ToArray()),
        ["puntos"] = (long)await qdrant.CountAsync(Prefijo + "dup", exact: true),
        ["puntos_esperados_como_sec_a"] = secA["puntos"]!.GetValue<long>()
    };
    Console.WriteLine($"duplicado: {informe["disparo_duplicado"]!.ToJsonString()}");

    informe["caracteres_por_subconjunto"] = caracteres;
    await File.WriteAllTextAsync(Path.Combine(salida, $"concurrencia-{caracteres}.json"), informe.ToJsonString(json));
    return 0;
}

static ((string Ruta, string Contenido)[] Tomados, List<(string Ruta, string Contenido)> Resto) Tomar(
    List<(string Ruta, string Contenido)> docs, int caracteres)
{
    string[] perfil = [".cs", ".md", ".json", ".ts", ".sql"];
    var tomados = new List<(string, string)>();
    var resto = new List<(string, string)>();
    var suma = 0;
    foreach (var d in docs)
    {
        // Como mucho 100 documentos: el maximo del contrato por solicitud.
        if (suma < caracteres && tomados.Count < 100 && perfil.Contains(Path.GetExtension(d.Ruta).ToLowerInvariant()) && d.Contenido.Length is > 200 and < 20_000)
        {
            tomados.Add(d);
            suma += d.Contenido.Length;
        }
        else resto.Add(d);
    }
    return (tomados.ToArray(), resto);
}

static double? Mediana(List<double> xs)
{
    if (xs.Count == 0) return null;
    var o = xs.Order().ToList();
    return o.Count % 2 == 1 ? o[o.Count / 2] : Math.Round((o[o.Count / 2 - 1] + o[o.Count / 2]) / 2, 3);
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Saturacion(string raiz, int n)
{
    var docs = await Escanear(raiz);
    var lotes = new List<(string, string)[]>();
    var resto = docs;
    for (var i = 0; i < n; i++)
    {
        // ~30 000 caracteres (unos segundos de TEI cada una): las n llegan antes de que termine la primera.
        (var lote, resto) = Tomar(resto, 30_000);
        lotes.Add(lote);
    }
    for (var i = 0; i < n; i++) await Registrar($"{Prefijo}sat-{i}");

    using var servicio = new ServicioEnProceso(qdrantHost, qdrantGrpc, teiUrl); // limite por defecto del servicio
    using var http = servicio.Cliente();
    var total = Stopwatch.StartNew();
    var tareas = lotes.Select((l, i) => Post(http, $"/api/colecciones/{Prefijo}sat-{i}/ingestas", CuerpoIngesta(l))).ToArray();
    await Task.WhenAll(tareas);
    var filas = new JsonArray(tareas.Select((t, i) => (JsonNode)new JsonObject
    {
        ["coleccion"] = $"{Prefijo}sat-{i}", ["documentos"] = lotes[i].Length, ["status"] = t.Result.Status,
        ["segundos"] = t.Result.Segundos, ["retry_after"] = t.Result.RetryAfter,
        ["error"] = t.Result.Status == 200 ? null : t.Result.Cuerpo?["errors"]?.ToJsonString()
    }).ToArray());
    var informe = new JsonObject
    {
        ["solicitudes"] = n, ["segundos_total"] = Math.Round(total.Elapsed.TotalSeconds, 1),
        ["status"] = new JsonObject(tareas.GroupBy(t => t.Result.Status).OrderBy(g => g.Key)
            .Select(g => KeyValuePair.Create(g.Key.ToString(), (JsonNode?)g.Count()))),
        ["detalle"] = filas
    };
    Console.WriteLine(informe.ToJsonString(json));
    await File.WriteAllTextAsync(Path.Combine(salida, "saturacion.json"), informe.ToJsonString(json));
    return 0;
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Limpiar()
{
    if (!File.Exists(registroCreadas)) { Console.WriteLine("Nada que borrar."); return 0; }
    foreach (var c in File.ReadAllLines(registroCreadas).Where(l => l.Length > 0).Distinct())
    {
        if (!c.StartsWith(Prefijo, StringComparison.Ordinal)) throw new InvalidOperationException($"Nombre ajeno en el registro: {c}");
        if (await qdrant.CollectionExistsAsync(c))
        {
            await qdrant.DeleteCollectionAsync(c);
            Console.WriteLine($"borrada {c}");
        }
        else Console.WriteLine($"no existe {c}");
    }
    File.Delete(registroCreadas);
    return 0;
}

// ---------------------------------------------------------------------------------------------------------------

/// <summary>
/// La API del servicio en Kestrel real (no TestServer: asi cuentan el socket, la serializacion y el limite de cuerpo),
/// con emisor JWT de prueba y concesiones sobre las colecciones it-1-7-*. Motor real contra el destino dado.
/// </summary>
sealed class ServicioEnProceso : WebApplicationFactory<Reyma.VectorSpace.Engine.Microservice.Api.Program>
{
    private const string Emisor = "https://consumo.prueba.invalid";
    private static readonly SymmetricSecurityKey Clave = new(RandomNumberGenerator.GetBytes(32));
    private readonly Dictionary<string, string?> _valores;

    public ServicioEnProceso(string qdrantHost, int qdrantGrpc, string teiUrl, Dictionary<string, string?>? extra = null)
    {
        _valores = new Dictionary<string, string?>
        {
            ["ASPNETCORE_BASEPATH"] = "", ["AllowedOrigins"] = "",
            ["IdentityServerAuthenticationOptions:Authority"] = "", ["PermissionsValidatorOptions:ApiUri"] = "",
            ["IntegrityServiceOptions:ApiUri"] = "",
            ["Serilog:MinimumLevel:Default"] = "Fatal", ["Serilog:MinimumLevel:Override:Microsoft"] = "Fatal",
            ["Serilog:MinimumLevel:Override:System"] = "Fatal",
            ["Motor:Qdrant:Host"] = qdrantHost, ["Motor:Qdrant:PuertoGrpc"] = qdrantGrpc.ToString(),
            ["Motor:Embeddings:UrlBase"] = teiUrl,
        };
        string[] colecciones = ["micro-repo", "cuerpo", "sec-a", "sec-b", "con-a", "con-b", "dup", .. Enumerable.Range(0, 64).Select(i => $"sat-{i}")];
        for (var i = 0; i < colecciones.Length; i++)
        {
            _valores[$"ControlDeAcceso:Concesiones:{i}:Coleccion"] = "it-1-7-" + colecciones[i];
            _valores[$"ControlDeAcceso:Concesiones:{i}:Operaciones:0"] = "Consultar";
            _valores[$"ControlDeAcceso:Concesiones:{i}:Operaciones:1"] = "Ingestar";
            _valores[$"ControlDeAcceso:Concesiones:{i}:Usuarios:0"] = "consumo";
        }
        foreach (var (k, v) in extra ?? []) _valores[k] = v;
        UseKestrel(0);
    }

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
            config.AddInMemoryCollection(_valores);
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
        StartServer();
        var client = CreateClient();
        client.Timeout = TimeSpan.FromHours(4);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emisor, Audience = ServicioInfo.NombreApi,
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["preferred_username"] = "consumo" },
            NotBefore = DateTime.UtcNow.AddMinutes(-1), IssuedAt = DateTime.UtcNow.AddMinutes(-1), Expires = DateTime.UtcNow.AddHours(6),
            SigningCredentials = new SigningCredentials(Clave, SecurityAlgorithms.HmacSha256)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>Bytes enviados y recibidos por todos los sockets del proceso (contadores de System.Net.Sockets).</summary>
sealed class ContadorSockets : EventListener
{
    private long _enviados, _recibidos;

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "System.Net.Sockets")
            EnableEvents(source, EventLevel.LogAlways, EventKeywords.All, new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "0.5" });
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName != "EventCounters" || e.Payload is null) return;
        foreach (var p in e.Payload.OfType<IDictionary<string, object>>())
        {
            if (!p.TryGetValue("Name", out var n) || !p.TryGetValue("Mean", out var m)) continue;
            var valor = Convert.ToInt64(m);
            if ((string)n == "bytes-sent") Interlocked.Exchange(ref _enviados, valor);
            else if ((string)n == "bytes-received") Interlocked.Exchange(ref _recibidos, valor);
        }
    }

    public async Task<(long Enviados, long Recibidos)> Leer()
    {
        await Task.Delay(1500);
        return (Interlocked.Read(ref _enviados), Interlocked.Read(ref _recibidos));
    }
}

/// <summary>Working set maximo del proceso mientras vive (muestreo cada 200 ms).</summary>
sealed class Muestreo : IDisposable
{
    private readonly Timer _timer;
    private long _maximo = Environment.WorkingSet;

    public Muestreo() => _timer = new Timer(_ =>
    {
        var ws = Environment.WorkingSet;
        long actual;
        while (ws > (actual = Interlocked.Read(ref _maximo)) && Interlocked.CompareExchange(ref _maximo, ws, actual) != actual) { }
    }, null, 0, 200);

    public long Maximo => Interlocked.Read(ref _maximo);

    public void Dispose() => _timer.Dispose();
}
