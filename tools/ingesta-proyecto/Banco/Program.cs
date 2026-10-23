// 1.7.3: ingesta de proyecto completo (URL Git o .zip) y seleccion de modelo. Ver ../README.md.
//
//   git                 clonado endurecido contra el servidor Git de prueba: permitido, rechazados por lista blanca,
//                       redireccion (endurecido vs git por defecto), identidad del servicio, tope de tamano y de plazo
//   escaner             escaner real de Rag.Context.Engine vs portado sobre los clones; checkout vs arbol de trabajo;
//                       junction; zip del mismo commit vs clone; documentos por lenguaje
//   ingesta <vse|rce>   URL Git -> clone -> escaner portado -> POST ingestas (motor real, TEI local); segunda pasada;
//                       despues el .zip del mismo commit a la misma coleccion
//   resumen             resumen de negocio con AltheIA (cliente HTTP de 1.13) sobre una muestra de Rag.Context.Engine,
//                       por modelo; tercer vector en una coleccion con dense + dense-resumen
//   modelos             GET /v1/models, modelo inexistente o de otro tipo, mezcla de modelos con la misma dimension,
//                       metadata de coleccion
//   limpiar             borra las colecciones it-1-7-3-* que este banco creo (y solo esas)
//
// Destino: Qdrant de docker-compose.motor.yml (127.0.0.1:16333 REST / 16334 gRPC) y su TEI (127.0.0.1:18080).
// AltheIA: ALTHEIA_KEY / ALTHEIA_CERT (variables de usuario, solo por nombre). Salida: .cache/ingesta-proyecto-1.7.3/.
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IngestaProyecto.Banco;
using IngestaProyecto.Prototipo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RagEngine.Core.Infrastructure.Scanning;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Chunking;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;

const string QdrantHost = "127.0.0.1";
const int QdrantGrpc = 16334;
const string QdrantRest = "http://127.0.0.1:16333";
const string TeiUrl = "http://127.0.0.1:18080";
var raizRepo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
var trabajo = Path.Combine(raizRepo, ".cache", "ingesta-proyecto-1.7.3");
var reposBare = Path.Combine(trabajo, "git-raiz");
var clones = Path.Combine(trabajo, "clones");
var registro = Path.Combine(trabajo, "creadas.txt");
Directory.CreateDirectory(clones);
var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var qdrant = new HttpClient { BaseAddress = new Uri(QdrantRest), Timeout = TimeSpan.FromMinutes(5) };
// ExtensionMap del escaner del origen, por nombre de lenguaje.
var Lenguaje = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [".cs"] = "CSharp", [".ts"] = "TypeScript", [".tsx"] = "TypeScript", [".js"] = "JavaScript", [".jsx"] = "JavaScript",
    [".xaml"] = "Xaml", [".sql"] = "Sql", [".md"] = "Markdown", [".txt"] = "PlainText", [".json"] = "PlainText",
    [".xml"] = "PlainText", [".csproj"] = "PlainText",
};

if (args.Length == 0) { Console.Error.WriteLine("Uso: git | escaner | ingesta <vse|rce> | resumen | modelos | limpiar"); return 2; }
return args[0] switch
{
    "git" => await Git(),
    "escaner" => await Escaner(),
    "ingesta" => await Ingesta(args[1]),
    "resumen" => await Resumen(),
    "modelos" => await Modelos(),
    "limpiar" => await Limpiar(),
    "sonda" => await Sonda(),
    "mezcla" => await Mezcla(args[1]),
    "chunks" => await Chunks(args[1]),
    _ => 2
};

// ---------------------------------------------------------------------------------------------------------------

async Task<string> GitHttpBackend()
{
    var psi = new ProcessStartInfo("git", "--exec-path") { RedirectStandardOutput = true, UseShellExecute = false };
    using var p = Process.Start(psi)!;
    var dir = (await p.StandardOutput.ReadToEndAsync()).Trim();
    return Path.Combine(dir, OperatingSystem.IsWindows() ? "git-http-backend.exe" : "git-http-backend");
}

async Task<string> GitSalida(string directorio, params string[] argumentos)
{
    var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = directorio };
    foreach (var a in argumentos) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    var s = await p.StandardOutput.ReadToEndAsync();
    await p.WaitForExitAsync();
    return s.Trim();
}

async Task Guardar(string nombre, JsonNode informe)
{
    var texto = informe.ToJsonString(json);
    await File.WriteAllTextAsync(Path.Combine(trabajo, nombre), texto);
    Console.WriteLine(texto.Length > 6000 ? texto[..6000] + "\n..." : texto);
}

static double Mb(long b) => Math.Round(b / 1048576d, 2);

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Git()
{
    await using var servidor = await ServidorGitDePrueba.IniciarAsync(reposBare, await GitHttpBackend());
    var p = servidor.Puerto;
    var b = $"http://127.0.0.1:{p}";
    OrigenGitPermitido[] lista =
    [
        new("http", "127.0.0.1", p, "/git/"), new("http", "127.0.0.1", p, "/privado/"),
        new("http", "127.0.0.1", p, "/redirige/"), new("http", "127.0.0.1", p, "/lento/"),
    ];
    var basico = new OpcionesClonado { PermitirHttp = true };
    var casos = new JsonArray();

    async Task<JsonObject> Caso(string nombre, string url, OpcionesClonado o, bool conservar = false)
    {
        var internasAntes = servidor.PeticionesInternas;
        var caso = new JsonObject { ["caso"] = nombre, ["url"] = url.Replace(p.ToString(), "<puerto>").Replace(servidor.PuertoInterno.ToString(), "<puerto-interno>") };
        var (uri, error) = ListaBlancaGit.Validar(url, lista);
        caso["lista_blanca"] = error ?? "admitida";
        if (uri is null)
        {
            caso["peticiones_al_interno"] = servidor.PeticionesInternas - internasAntes;
            casos.Add(caso);
            return caso;
        }
        var destino = Path.Combine(clones, nombre);
        ClonadorGit.Borrar(destino);
        var r = await ClonadorGit.ClonarAsync(uri, destino, o);
        caso["exito"] = r.Exito; caso["motivo"] = r.Motivo; caso["codigo_salida"] = r.CodigoSalida;
        caso["segundos"] = r.Segundos; caso["mb_en_disco"] = Mb(r.Bytes);
        caso["stderr"] = string.Join(" | ", r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Take(4))
            .Replace(p.ToString(), "<puerto>").Replace(servidor.PuertoInterno.ToString(), "<puerto-interno>");
        caso["peticiones_al_interno"] = servidor.PeticionesInternas - internasAntes;
        if (r.Exito)
        {
            caso["commit"] = await GitSalida(destino, "rev-parse", "HEAD");
            caso["superficial"] = await GitSalida(destino, "rev-parse", "--is-shallow-repository");
            caso["commits_locales"] = await GitSalida(destino, "rev-list", "--count", "HEAD");
            caso["archivos_en_checkout"] = (await GitSalida(destino, "ls-files")).Split('\n').Length;
        }
        if (!conservar) ClonadorGit.Borrar(destino);
        casos.Add(caso);
        return caso;
    }

    await Caso("vse", $"{b}/git/vse.git", basico, conservar: true);
    await Caso("rce", $"{b}/git/rce.git", basico, conservar: true);
    await Caso("rce-historial-completo", $"{b}/git/rce.git", basico with { Profundidad = 1_000_000 });

    // Rechazados por la lista blanca: no llegan a la red.
    await Caso("rechazado-github", "https://github.com/DanielWueno/Rag.Context.Engine.git", basico);
    await Caso("rechazado-puerto-interno", $"http://127.0.0.1:{servidor.PuertoInterno}/git/vse.git", basico);
    await Caso("rechazado-qdrant-rest", "http://127.0.0.1:16333/collections", basico);
    await Caso("rechazado-alias-localhost", $"http://localhost:{p}/git/vse.git", basico);
    await Caso("rechazado-ruta-fuera-de-prefijo", $"{b}/otro/vse.git", basico);
    await Caso("rechazado-credenciales-en-url", $"http://usuario:clave@127.0.0.1:{p}/git/vse.git", basico);

    // Redireccion desde un host permitido hacia uno que no lo esta.
    await Caso("redireccion-endurecido", $"{b}/redirige/vse.git", basico);
    await Caso("redireccion-git-por-defecto", $"{b}/redirige/vse.git", basico with { SeguirRedireccionesComoGitPorDefecto = true });

    // Identidad: el repo privado exige la del servicio. Sin ella, falla sin preguntar ni usar Git Credential Manager.
    await Caso("privado-sin-identidad", $"{b}/privado/vse.git", basico);
    var conIdentidad = await Caso("privado-identidad-del-servicio", $"{b}/privado/vse.git", basico with { CabeceraAutorizacion = servidor.CabeceraServicio }, conservar: true);
    var dirPrivado = Path.Combine(clones, "privado-identidad-del-servicio");
    if (Directory.Exists(dirPrivado))
    {
        var config = await File.ReadAllTextAsync(Path.Combine(dirPrivado, ".git", "config"));
        var todo = string.Join("\n", Directory.EnumerateFiles(Path.Combine(dirPrivado, ".git"), "*", SearchOption.AllDirectories)
            .Where(f => new FileInfo(f).Length < 1_000_000).Select(f => { try { return File.ReadAllText(f); } catch (IOException) { return ""; } }));
        conIdentidad["clave_en_git_config"] = config.Contains(servidor.ClaveServicio, StringComparison.Ordinal);
        conIdentidad["clave_en_algun_archivo_de_.git"] = todo.Contains(servidor.ClaveServicio, StringComparison.Ordinal)
            || todo.Contains(servidor.CabeceraServicio, StringComparison.Ordinal);
        conIdentidad["clave_en_argumentos"] = false; // ClonadorGit la pasa por GIT_CONFIG_VALUE_0, nunca en ArgumentList
        ClonadorGit.Borrar(dirPrivado);
    }

    // Que credential.helper veria git con y sin el aislamiento del clonador.
    var vacio = Path.Combine(Path.GetTempPath(), "vse-git-vacio", "gitconfig");
    var helperPorDefecto = await GitSalida(trabajo, "config", "--get-all", "credential.helper");
    var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false };
    foreach (var a in new[] { "config", "--get-all", "credential.helper" }) psi.ArgumentList.Add(a);
    psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1"; psi.Environment["GIT_CONFIG_GLOBAL"] = vacio;
    using (var pr = Process.Start(psi)!) { var aislado = (await pr.StandardOutput.ReadToEndAsync()).Trim(); await pr.WaitForExitAsync();
        casos.Add(new JsonObject { ["caso"] = "credential-helper", ["por_defecto"] = helperPorDefecto, ["aislado"] = aislado }); }

    // Topes.
    await Caso("tope-de-tamano", $"{b}/git/rce.git", basico with { MaxBytes = 2L * 1024 * 1024 });
    await Caso("tope-de-plazo", $"{b}/lento/vse.git", basico with { Plazo = TimeSpan.FromSeconds(5) });

    await Guardar("git.json", new JsonObject
    {
        ["git"] = await GitSalida(trabajo, "--version"),
        ["repos_bare_mb"] = new JsonObject
        {
            ["vse.git"] = Mb(ClonadorGit.TamanoDirectorio(Path.Combine(reposBare, "vse.git"))),
            ["rce.git"] = Mb(ClonadorGit.TamanoDirectorio(Path.Combine(reposBare, "rce.git")))
        },
        ["casos"] = casos
    });
    return 0;
}

// ---------------------------------------------------------------------------------------------------------------

async Task<List<string>> EscanerReal(string raiz)
{
    var s = new FileSystemIngestionScanner(NullLogger<FileSystemIngestionScanner>.Instance);
    var rutas = new List<string>();
    await foreach (var a in s.ScanAsync(raiz, RagEngine.Core.Domain.ScanProfile.DotNetEnterprise))
        rutas.Add(a.RelativePath.Replace('\\', '/'));
    return rutas;
}

async Task<int> Escaner()
{
    var informe = new JsonObject();
    foreach (var (repo, arbol) in new[] { ("vse", raizRepo), ("rce", Path.GetFullPath(Path.Combine(raizRepo, "..", "Rag.Context.Engine"))) })
    {
        var checkout = Path.Combine(clones, repo);
        var sw = Stopwatch.StartNew();
        var real = await EscanerReal(checkout);
        var tReal = sw.Elapsed.TotalSeconds; sw.Restart();
        var portado = EscanerDeCheckout.Escanear(checkout, PerfilEscaneo.DotNetEnterprise);
        var tPortado = sw.Elapsed.TotalSeconds;
        var rutasPortado = portado.Select(a => a.RutaRelativa.Replace('\\', '/')).ToList();
        var enArbol = await EscanerReal(arbol);
        var enArbolPortado = EscanerDeCheckout.Escanear(arbol, PerfilEscaneo.DotNetEnterprise).Select(a => a.RutaRelativa.Replace('\\', '/')).ToList();

        var porLenguaje = portado.GroupBy(a => Lenguaje.GetValueOrDefault(Path.GetExtension(a.RutaRelativa), "?"))
            .OrderByDescending(g => g.Count())
            .Select(g => KeyValuePair.Create(g.Key, (JsonNode?)new JsonObject { ["documentos"] = g.Count(), ["mb"] = Mb(g.Sum(a => a.Bytes)) }));
        informe[repo] = new JsonObject
        {
            ["escaner_real"] = new JsonObject { ["documentos"] = real.Count, ["segundos"] = Math.Round(tReal, 3) },
            ["escaner_portado"] = new JsonObject { ["documentos"] = portado.Count, ["segundos"] = Math.Round(tPortado, 3), ["mb"] = Mb(portado.Sum(a => a.Bytes)) },
            ["mismas_rutas_y_orden"] = real.SequenceEqual(rutasPortado),
            ["arbol_de_trabajo"] = new JsonObject
            {
                ["documentos"] = enArbol.Count,
                ["solo_en_arbol"] = enArbol.Except(real).Count(),
                ["solo_en_checkout"] = real.Except(enArbol).Count(),
                ["muestra_solo_en_arbol"] = new JsonArray(enArbol.Except(real).Take(8).Select(x => (JsonNode)x).ToArray()),
                ["documentos_escaner_portado"] = enArbolPortado.Count,
                ["solo_en_arbol_con_portado"] = enArbolPortado.Except(real).Count(),
                ["el_original_lee_y_el_portado_no"] = new JsonArray(enArbol.Except(enArbolPortado)
                    .GroupBy(x => x.Split('/')[0]).Select(g => (JsonNode)$"{g.Key} ({g.Count()})").ToArray())
            },
            ["por_lenguaje"] = new JsonObject(porLenguaje)
        };
    }

    // Junction: el escaner real del origen sigue el enlace; el portado no (Prototipo enlaces lo crea).
    var conJunction = Path.Combine(trabajo, "enlaces-windows", "repo");
    if (Directory.Exists(conJunction))
        informe["junction"] = new JsonObject
        {
            ["escaner_real"] = new JsonArray((await EscanerReal(conJunction)).Select(x => (JsonNode)x).ToArray()),
            ["escaner_portado"] = new JsonArray(EscanerDeCheckout.Escanear(conJunction, PerfilEscaneo.DotNetEnterprise)
                .Select(a => (JsonNode)a.RutaRelativa.Replace('\\', '/')).ToArray())
        };

    // Zip del mismo commit (git archive desde el bare) vs el clone: mismos documentos y mismo contenido.
    var zip = Path.Combine(trabajo, "vse-commit.zip");
    await GitSalida(Path.Combine(reposBare, "vse.git"), "archive", "--format=zip", "--prefix=vse/", "-o", zip, "HEAD");
    ResultadoZip rz;
    var swz = Stopwatch.StartNew();
    using (var f = File.OpenRead(zip)) rz = LectorZip.Leer(f, PerfilEscaneo.DotNetEnterprise, new LimitesZip(), "vse");
    var tZip = swz.Elapsed.TotalSeconds;
    var clone = EscanerDeCheckout.Escanear(Path.Combine(clones, "vse"), PerfilEscaneo.DotNetEnterprise)
        .ToDictionary(a => a.RutaRelativa.Replace('\\', '/'), a => File.ReadAllText(a.RutaAbsoluta));
    var deZip = rz.Documentos.ToDictionary(d => d.Ruta, d => d.Contenido);
    static string N(string s) => s.Replace("\r\n", "\n");
    informe["zip_vs_clone_vse"] = new JsonObject
    {
        ["bytes_zip"] = new FileInfo(zip).Length, ["leer_zip_segundos"] = Math.Round(tZip, 3),
        ["documentos_zip"] = deZip.Count, ["documentos_clone"] = clone.Count,
        ["solo_en_zip"] = new JsonArray(deZip.Keys.Except(clone.Keys).Take(10).Select(x => (JsonNode)x).ToArray()),
        ["solo_en_clone"] = new JsonArray(clone.Keys.Except(deZip.Keys).Take(10).Select(x => (JsonNode)x).ToArray()),
        ["contenido_identico"] = deZip.Keys.Intersect(clone.Keys).Count(k => deZip[k] == clone[k]),
        ["identico_salvo_fin_de_linea"] = deZip.Keys.Intersect(clone.Keys).Count(k => deZip[k] != clone[k] && N(deZip[k]) == N(clone[k])),
        ["distinto"] = deZip.Keys.Intersect(clone.Keys).Count(k => N(deZip[k]) != N(clone[k])),
    };

    // Reglas de la raiz que empiezan por punto: original (TrimStart('.', '/')) vs portado.
    string[] reglas = [".cache/", ".mcp.json", ".claude/settings.local.json", "docs/eval", "bin/"];
    var original = RagEngine.Core.Infrastructure.Scanning.IgnoreRules.FromLines(reglas, ".gitignore");
    var corregido = IngestaProyecto.Prototipo.IgnoreRules.FromLines(reglas, ".gitignore");
    informe["ignore_rules_punto_inicial"] = new JsonArray(new[] { ".cache/x.json", ".mcp.json", ".claude/settings.local.json", "sub/.mcp.json", "docs/eval/a.json", "./bin/a.dll", "src/Program.cs" }
        .Select(r => (JsonNode)new JsonObject { ["ruta"] = r, ["original"] = original.IsIgnored(r, false), ["portado"] = corregido.IsIgnored(r, false) }).ToArray());

    // --lang del CLI: se declara pero no llega a IngestionRequest (src/RagEngine.Cli/Commands/IngestCommand.cs).
    var ingest = await File.ReadAllTextAsync(Path.Combine(raizRepo, "..", "Rag.Context.Engine", "src", "RagEngine.Cli", "Commands", "IngestCommand.cs"));
    informe["cli_lang"] = new JsonObject
    {
        ["apariciones_de_LanguageFilter"] = System.Text.RegularExpressions.Regex.Matches(ingest, "LanguageFilter").Count,
        ["se_pasa_a_IngestionRequest"] = ingest[ingest.IndexOf("new IngestionRequest(", StringComparison.Ordinal)..].Split(");")[0].Contains("LanguageFilter")
    };
    await Guardar("escaner.json", informe);
    return 0;
}

// ---------------------------------------------------------------------------------------------------------------

async Task<(int Status, JsonNode? Cuerpo, double Segundos)> Post(HttpClient http, string ruta, object cuerpo)
{
    var sw = Stopwatch.StartNew();
    using var r = await http.PostAsJsonAsync(ruta, cuerpo);
    var texto = await r.Content.ReadAsStringAsync();
    JsonNode? nodo = null;
    try { nodo = string.IsNullOrWhiteSpace(texto) ? null : JsonNode.Parse(texto); } catch (JsonException) { nodo = texto; }
    return ((int)r.StatusCode, nodo, Math.Round(sw.Elapsed.TotalSeconds, 2));
}

static object CuerpoIngesta(IEnumerable<(string Ruta, string Contenido)> lote) =>
    new { origen = new { tipo = "documentos", documentos = lote.Select(d => new { ruta = d.Ruta, contenido = d.Contenido }) } };

async Task Registrar(string coleccion)
{
    if (!coleccion.StartsWith(ServicioEnProceso.Prefijo, StringComparison.Ordinal)) throw new InvalidOperationException(coleccion);
    var ya = File.Exists(registro) ? await File.ReadAllLinesAsync(registro) : [];
    if (!ya.Contains(coleccion)) await File.AppendAllLinesAsync(registro, [coleccion]);
}

async Task<long> Puntos(string coleccion)
{
    var r = await qdrant.PostAsJsonAsync($"/collections/{coleccion}/points/count", new { exact = true });
    if (!r.IsSuccessStatusCode) return -1;
    return (long)JsonNode.Parse(await r.Content.ReadAsStringAsync())!["result"]!["count"]!;
}

async Task<JsonObject> IngerirPorLotes(HttpClient http, string coleccion, List<(string Ruta, string Contenido)> docs)
{
    var sw = Stopwatch.StartNew();
    int indexados = 0, sinCambios = 0, omitidos = 0, solicitudes = 0;
    long maxWs = Environment.WorkingSet;
    foreach (var lote in docs.Chunk(100))
    {
        var (status, r, _) = await Post(http, $"/api/colecciones/{coleccion}/ingestas", CuerpoIngesta(lote));
        solicitudes++;
        if (status != 200) return new JsonObject { ["error"] = $"{status} {r?.ToJsonString()}" };
        indexados += (int)r!["documentosIndexados"]!; sinCambios += (int)r["documentosSinCambios"]!; omitidos += (int)r["documentosOmitidos"]!;
        maxWs = Math.Max(maxWs, Environment.WorkingSet);
    }
    return new JsonObject
    {
        ["solicitudes"] = solicitudes, ["segundos"] = Math.Round(sw.Elapsed.TotalSeconds, 1), ["indexados"] = indexados,
        ["sin_cambios"] = sinCambios, ["omitidos"] = omitidos, ["puntos"] = await Puntos(coleccion), ["working_set_max_mb"] = Mb(maxWs)
    };
}

async Task<int> Ingesta(string repo)
{
    var coleccion = ServicioEnProceso.Prefijo + repo;
    await Registrar(coleccion);
    if (await Puntos(coleccion) >= 0) { Console.Error.WriteLine($"{coleccion} ya existe: 'limpiar' antes."); return 1; }

    await using var servidor = await ServidorGitDePrueba.IniciarAsync(reposBare, await GitHttpBackend());
    var url = $"http://127.0.0.1:{servidor.Puerto}/git/{repo}.git";
    var (uri, error) = ListaBlancaGit.Validar(url, [new("http", "127.0.0.1", servidor.Puerto, "/git/")]);
    if (uri is null) throw new InvalidOperationException(error);

    var fases = new JsonObject();
    var destino = Path.Combine(trabajo, "ingesta-checkout-" + repo);
    ClonadorGit.Borrar(destino);
    var c = await ClonadorGit.ClonarAsync(uri, destino, new OpcionesClonado { PermitirHttp = true });
    fases["clonar"] = new JsonObject { ["exito"] = c.Exito, ["segundos"] = c.Segundos, ["mb"] = Mb(c.Bytes) };
    if (!c.Exito) { await Guardar($"ingesta-{repo}.json", fases); return 1; }

    var sw = Stopwatch.StartNew();
    var archivos = EscanerDeCheckout.Escanear(destino, PerfilEscaneo.DotNetEnterprise);
    var docs = new List<(string Ruta, string Contenido)>();
    foreach (var a in archivos) docs.Add((a.RutaRelativa.Replace('\\', '/'), await File.ReadAllTextAsync(a.RutaAbsoluta)));
    fases["escanear_y_leer"] = new JsonObject { ["documentos"] = docs.Count, ["segundos"] = Math.Round(sw.Elapsed.TotalSeconds, 3), ["mb"] = Mb(docs.Sum(d => (long)Encoding.UTF8.GetByteCount(d.Contenido))) };

    using var servicio = new ServicioEnProceso(QdrantHost, QdrantGrpc, PerfilEmbeddings.TeiArctic(TeiUrl));
    using var http = servicio.Cliente();
    fases["ingesta_primera"] = await IngerirPorLotes(http, coleccion, docs);
    fases["ingesta_segunda"] = await IngerirPorLotes(http, coleccion, docs);

    // El .zip del mismo commit, a la MISMA coleccion: si los dos origenes producen los mismos documentos, todo "sin cambios".
    var zip = Path.Combine(trabajo, $"{repo}-commit.zip");
    await GitSalida(Path.Combine(reposBare, $"{repo}.git"), "archive", "--format=zip", $"--prefix={repo}/", "-o", zip, "HEAD");
    sw.Restart();
    ResultadoZip rz;
    using (var f = File.OpenRead(zip)) rz = LectorZip.Leer(f, PerfilEscaneo.DotNetEnterprise, new LimitesZip(), repo);
    fases["zip_leer"] = new JsonObject { ["bytes_zip"] = new FileInfo(zip).Length, ["documentos"] = rz.Documentos.Count, ["error"] = rz.Error, ["segundos"] = Math.Round(sw.Elapsed.TotalSeconds, 3) };
    fases["zip_misma_coleccion"] = await IngerirPorLotes(http, coleccion, rz.Documentos.Select(d => (d.Ruta, d.Contenido)).ToList());

    ClonadorGit.Borrar(destino);
    fases["checkout_borrado"] = !Directory.Exists(destino);
    await Guardar($"ingesta-{repo}.json", new JsonObject { ["coleccion"] = coleccion, ["perfil"] = "tei-arctic-256 (docker-compose.motor.yml)", ["fases"] = fases });
    return 0;
}

// ---------------------------------------------------------------------------------------------------------------

const string SystemPromptResumen = """
    Eres un analista de negocio que traduce fragmentos de código — en cualquier lenguaje de
    programación — a descripciones de comportamiento para usuarios sin conocimiento técnico.

    No expliques sintaxis, nombres de frameworks ni construcciones del lenguaje. A partir de tu
    propio conocimiento de cómo funciona el lenguaje indicado en el contexto, identifica QUÉ hace
    este fragmento, QUÉ dato o acción involucra, y QUÉ regla o condición aplica — en el lenguaje
    que usaría alguien que jamás vio código.

    Responde en español, en 1 a 3 oraciones (máximo 60 palabras). Empieza nombrando la entidad,
    pantalla o archivo, seguido de dos puntos — sin frases como "este componente" o "esta clase
    representa". No inventes campos, reglas o comportamientos que no estén explícitos en el
    fragmento: si no está en el código, no existe.

    Si el fragmento es puramente técnico sin significado de negocio visible (imports,
    configuración, boilerplate, getters/setters triviales), responde exactamente:
    SIN_CONTENIDO_DE_NEGOCIO
    """;

async Task<List<CodeChunk>> MuestraDeChunks(IServiceProvider sp, string raiz, int n)
{
    var router = sp.GetRequiredService<ChunkingStrategyRouter>();
    var opciones = new ChunkingOptions { MaxTokensPerChunk = 1024, OverlapTokens = 64, RepositoryName = "rce", BatchSize = 32 };
    var todos = new List<CodeChunk>();
    foreach (var a in EscanerDeCheckout.Escanear(raiz, PerfilEscaneo.DotNetEnterprise)
                 .Where(a => a.RutaRelativa.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     && a.RutaRelativa.Replace('\\', '/').StartsWith("src/", StringComparison.Ordinal))
                 .OrderBy(a => a.RutaRelativa, StringComparer.Ordinal))
    {
        var contenido = await File.ReadAllTextAsync(a.RutaAbsoluta);
        var artefacto = DocumentArtifacts.TryCreate(a.RutaRelativa.Replace('\\', '/'), contenido, ScanProfile.DotNetEnterprise, DateTimeOffset.UnixEpoch);
        if (artefacto is null) continue;
        await foreach (var ch in router.GetStrategy(artefacto).ChunkAsync(artefacto, contenido, opciones, CancellationToken.None))
            if (ch.Content.Trim().Length >= 400) todos.Add(ch);
    }
    // Determinista: uno de cada k, repartido por todo src/.
    var k = Math.Max(1, todos.Count / n);
    return todos.Where((_, i) => i % k == 0).Take(n).ToList();
}

async Task<JsonObject> Chat(HttpClient http, string modelo, string usuario, int maxTokens, bool? pensar)
{
    var cuerpo = new JsonObject
    {
        ["model"] = modelo, ["temperature"] = 0.15, ["top_p"] = 0.95, ["max_tokens"] = maxTokens,
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = SystemPromptResumen },
            new JsonObject { ["role"] = "user", ["content"] = usuario })
    };
    if (pensar is bool b) cuerpo["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = b };
    var sw = Stopwatch.StartNew();
    using var r = await http.PostAsync("v1/chat/completions", new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json"));
    var texto = await r.Content.ReadAsStringAsync();
    var fila = new JsonObject { ["status"] = (int)r.StatusCode, ["segundos"] = Math.Round(sw.Elapsed.TotalSeconds, 2) };
    if (!r.IsSuccessStatusCode) { fila["error"] = texto.Length > 300 ? texto[..300] : texto; return fila; }
    var n = JsonNode.Parse(texto)!;
    var mensaje = n["choices"]![0]!["message"]!;
    var contenido = (string?)mensaje["content"] ?? "";
    fila["prompt_tokens"] = (int?)n["usage"]?["prompt_tokens"];
    fila["completion_tokens"] = (int?)n["usage"]?["completion_tokens"];
    fila["finish_reason"] = (string?)n["choices"]![0]!["finish_reason"];
    fila["tiene_think"] = contenido.Contains("<think>", StringComparison.Ordinal) || mensaje["reasoning_content"] is not null;
    var limpio = System.Text.RegularExpressions.Regex.Replace(contenido, "<think>.*?(</think>|$)", "", System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
    fila["resumen"] = limpio;
    fila["sin_negocio"] = limpio.StartsWith("SIN_CONTENIDO_DE_NEGOCIO", StringComparison.Ordinal);
    fila["vacio"] = limpio.Length == 0;
    return fila;
}

async Task<int> Resumen()
{
    using var servicio = new ServicioEnProceso(QdrantHost, QdrantGrpc, PerfilEmbeddings.AltheiaBgeM3(1024));
    using var altheia = servicio.ClienteDelSidecar();
    var muestra = await MuestraDeChunks(servicio.Services, Path.Combine(clones, "rce"), 30);
    await File.WriteAllTextAsync(Path.Combine(trabajo, "resumen-muestra.json"), new JsonArray(muestra.Select(c => (JsonNode)new JsonObject
    {
        ["ruta"] = c.Metadata.RelativeFilePath, ["lineas"] = $"{c.Metadata.StartLine}-{c.Metadata.EndLine}",
        ["content_hash"] = c.ContentHash, ["caracteres"] = c.Content.Length, ["contenido"] = c.Content
    }).ToArray()).ToJsonString(json));

    var configuraciones = new (string Nombre, string Modelo, int MaxTokens, bool? Pensar)[]
    {
        ("fast-160", "qwen3.8-27b-fast", 160, null),
        ("deep-160", "qwen3.8-27b-deep", 160, null),
        ("deep-160-sin-razonar", "qwen3.8-27b-deep", 160, false),
    };
    var informe = new JsonObject { ["muestra"] = muestra.Count, ["origen_muestra"] = "Rag.Context.Engine aae1de8, src/**/*.cs, chunks de 1024 con >= 400 caracteres, 1 de cada k" };
    var porConfig = new JsonObject();
    var mejores = new Dictionary<string, string>();
    foreach (var (nombre, modelo, maxTokens, pensar) in configuraciones)
    {
        var filas = new JsonArray();
        var sw = Stopwatch.StartNew();
        foreach (var ch in muestra)
        {
            var usuario = $"[Contexto: Lenguaje: {ch.Metadata.Language}, Ruta: {ch.Metadata.RelativeFilePath}]\n\n{ch.Content}";
            var f = await Chat(altheia, modelo, usuario, maxTokens, pensar);
            f["ruta"] = $"{ch.Metadata.RelativeFilePath}:{ch.Metadata.StartLine}";
            filas.Add(f);
            if (nombre == "fast-160" && (string?)f["resumen"] is { Length: > 0 } s && !(bool)f["sin_negocio"]!) mejores[ch.ContentHash] = s;
            Console.WriteLine($"{nombre} {f["ruta"]} {f["status"]} {f["segundos"]}s {f["finish_reason"]} think={f["tiene_think"]}");
        }
        var ok = filas.Where(f => (int)f!["status"]! == 200).ToList();
        porConfig[nombre] = new JsonObject
        {
            ["modelo"] = modelo, ["max_tokens"] = maxTokens, ["enable_thinking"] = pensar is null ? "por defecto" : pensar.ToString(),
            ["segundos_total"] = Math.Round(sw.Elapsed.TotalSeconds, 1),
            ["ok"] = ok.Count, ["vacios"] = ok.Count(f => (bool)f!["vacio"]!), ["sin_negocio"] = ok.Count(f => (bool)f!["sin_negocio"]!),
            ["cortados_por_longitud"] = ok.Count(f => (string?)f!["finish_reason"] == "length"),
            ["con_razonamiento"] = ok.Count(f => (bool)f!["tiene_think"]!),
            ["prompt_tokens"] = ok.Sum(f => (int?)f!["prompt_tokens"] ?? 0), ["completion_tokens"] = ok.Sum(f => (int?)f!["completion_tokens"] ?? 0),
            ["segundos_mediana"] = ok.Count == 0 ? null : ok.Select(f => (double)f!["segundos"]!).Order().ElementAt(ok.Count / 2),
            ["filas"] = filas
        };
    }
    informe["configuraciones"] = porConfig;

    // Tercer vector: el resumen embebido con el mismo modelo que el chunk, en una coleccion con dense + dense-resumen.
    const string coleccion = ServicioEnProceso.Prefijo + "resumen";
    await Registrar(coleccion);
    await qdrant.DeleteAsync($"/collections/{coleccion}");
    var brain = servicio.Services.GetRequiredService<Reyma.VectorSpace.Engine.Microservice.Infrastructure.Abstractions.IVectorizationBrain>();
    var crear = await qdrant.PutAsJsonAsync($"/collections/{coleccion}", new
    {
        vectors = new Dictionary<string, object>
        {
            ["dense"] = new { size = brain.EmbeddingDimensions, distance = "Cosine" },
            ["dense-resumen"] = new { size = brain.EmbeddingDimensions, distance = "Cosine" }
        }
    });
    var conResumen = muestra.Where(c => mejores.ContainsKey(c.ContentHash)).ToList();
    var swv = Stopwatch.StartNew();
    var dense = await brain.EmbedDocumentsAsync(conResumen.Select(c => c.EnrichedContent).ToArray(), CancellationToken.None);
    var resumenes = await brain.EmbedDocumentsAsync(conResumen.Select(c => mejores[c.ContentHash]).ToArray(), CancellationToken.None);
    var tVect = swv.Elapsed.TotalSeconds;
    var puntos = conResumen.Select((c, i) => new
    {
        id = c.Id, vector = new Dictionary<string, float[]> { ["dense"] = dense[i], ["dense-resumen"] = resumenes[i] },
        payload = new { file_path = c.Metadata.RelativeFilePath, resumen = mejores[c.ContentHash], resumen_modelo = "qwen3.8-27b-fast" }
    }).ToArray();
    var upsert = await qdrant.PutAsJsonAsync($"/collections/{coleccion}/points?wait=true", new { points = puntos });
    var scroll = JsonNode.Parse(await (await qdrant.PostAsJsonAsync($"/collections/{coleccion}/points/scroll", new { limit = 100, with_vector = true, with_payload = false })).Content.ReadAsStringAsync())!;
    var conAmbos = scroll["result"]!["points"]!.AsArray().Count(pt => pt!["vector"]?["dense"] is not null && pt["vector"]?["dense-resumen"] is not null);

    // Sin el tercer vector desde la creacion: Qdrant no deja agregarlo despues.
    const string sinResumen = ServicioEnProceso.Prefijo + "tenant";
    await Registrar(sinResumen);
    await qdrant.DeleteAsync($"/collections/{sinResumen}");
    await qdrant.PutAsJsonAsync($"/collections/{sinResumen}", new { vectors = new Dictionary<string, object> { ["dense"] = new { size = 8, distance = "Cosine" } } });
    var agregar = await qdrant.PatchAsJsonAsync($"/collections/{sinResumen}", new { vectors = new Dictionary<string, object> { ["dense-resumen"] = new { on_disk = false } } });
    var info = JsonNode.Parse(await (await qdrant.GetAsync($"/collections/{sinResumen}")).Content.ReadAsStringAsync())!;

    // Busqueda por la banda de resumen con una pregunta de negocio (informativo).
    var q = await brain.EmbedQueryAsync("¿cómo se decide qué colecciones puede consultar un usuario?", CancellationToken.None);
    var busq = JsonNode.Parse(await (await qdrant.PostAsJsonAsync($"/collections/{coleccion}/points/query", new { query = q, @using = "dense-resumen", limit = 3, with_payload = true })).Content.ReadAsStringAsync())!;

    informe["tercer_vector"] = new JsonObject
    {
        ["coleccion_creada"] = (int)crear.StatusCode, ["upsert"] = (int)upsert.StatusCode,
        ["puntos_con_resumen"] = conResumen.Count, ["puntos_con_dense_y_dense_resumen"] = conAmbos,
        ["vectorizar_segundos"] = Math.Round(tVect, 2), ["modelo_embeddings"] = brain.ModelName, ["dimension"] = brain.EmbeddingDimensions,
        ["agregar_vector_a_coleccion_existente"] = new JsonObject
        {
            ["status"] = (int)agregar.StatusCode, ["respuesta"] = await agregar.Content.ReadAsStringAsync(),
            ["vectores_tras_intentarlo"] = info["result"]?["config"]?["params"]?["vectors"]?.ToJsonString()
        },
        ["busqueda_por_resumen_top3"] = new JsonArray((busq["result"]?["points"]?.AsArray() ?? []).Select(pt => (JsonNode)new JsonObject
        {
            ["score"] = (double)pt!["score"]!, ["file_path"] = (string?)pt["payload"]?["file_path"], ["resumen"] = (string?)pt["payload"]?["resumen"]
        }).ToArray())
    };
    await Guardar("resumen.json", informe);
    return 0;
}

// ---------------------------------------------------------------------------------------------------------------

async Task<int> Modelos()
{
    var informe = new JsonObject();
    using (var servicio = new ServicioEnProceso(QdrantHost, QdrantGrpc, PerfilEmbeddings.AltheiaBgeM3(1024)))
    {
        using var altheia = servicio.ClienteDelSidecar();
        var tiempos = new List<double>();
        JsonNode? modelos = null;
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            var r = await altheia.GetAsync("v1/models");
            tiempos.Add(Math.Round(sw.Elapsed.TotalSeconds, 3));
            modelos = JsonNode.Parse(await r.Content.ReadAsStringAsync());
        }
        informe["v1_models"] = new JsonObject
        {
            ["segundos"] = new JsonArray(tiempos.Select(t => (JsonNode)t).ToArray()),
            ["modelos"] = new JsonArray(modelos!["data"]!.AsArray().Select(m => (JsonNode)new JsonObject
            {
                ["id"] = (string?)m!["id"], ["max_model_len"] = (int?)m["max_model_len"], ["owned_by"] = (string?)m["owned_by"],
                ["claves"] = string.Join(",", m.AsObject().Select(kv => kv.Key))
            }).ToArray())
        };

        async Task<JsonObject> Llamar(string ruta, object cuerpo)
        {
            var sw = Stopwatch.StartNew();
            var r = await altheia.PostAsJsonAsync(ruta, cuerpo);
            var t = await r.Content.ReadAsStringAsync();
            return new JsonObject { ["status"] = (int)r.StatusCode, ["segundos"] = Math.Round(sw.Elapsed.TotalSeconds, 2), ["cuerpo"] = t.Length > 400 ? t[..400] : t };
        }
        informe["embeddings_modelo_inexistente"] = await Llamar("v1/embeddings", new { model = "modelo-que-no-existe", input = new[] { "hola" } });
        informe["embeddings_con_modelo_de_chat"] = await Llamar("v1/embeddings", new { model = "qwen3.8-27b-fast", input = new[] { "hola" } });
        informe["chat_con_modelo_de_embeddings"] = await Llamar("v1/chat/completions", new { model = "bge-m3", max_tokens = 5, messages = new[] { new { role = "user", content = "hola" } } });
        informe["chat_modelo_inexistente"] = await Llamar("v1/chat/completions", new { model = "modelo-que-no-existe", max_tokens = 5, messages = new[] { new { role = "user", content = "hola" } } });
    }

    // Metadata de coleccion en el Qdrant 1.19.2 desplegado: donde guardar el perfil de la coleccion.
    const string meta = ServicioEnProceso.Prefijo + "metadatos";
    await Registrar(meta);
    await qdrant.DeleteAsync($"/collections/{meta}");
    var crear = await qdrant.PutAsJsonAsync($"/collections/{meta}", new
    {
        vectors = new Dictionary<string, object> { ["dense"] = new { size = 256, distance = "Cosine" } },
        metadata = new { perfil_embeddings = "tei-arctic-256", modelo = "Snowflake/snowflake-arctic-embed-m-v2.0", dimension = 256, resumen = false }
    });
    var info = JsonNode.Parse(await (await qdrant.GetAsync($"/collections/{meta}")).Content.ReadAsStringAsync())!;
    var actualizar = await qdrant.PatchAsJsonAsync($"/collections/{meta}", new { metadata = new { llm_resumen = "qwen3.8-27b-fast" } });
    var info2 = JsonNode.Parse(await (await qdrant.GetAsync($"/collections/{meta}")).Content.ReadAsStringAsync())!;
    informe["metadata_de_coleccion"] = new JsonObject
    {
        ["crear_status"] = (int)crear.StatusCode, ["leida"] = info["result"]?["config"]?["metadata"]?.DeepClone(),
        ["patch_status"] = (int)actualizar.StatusCode, ["tras_patch"] = info2["result"]?["config"]?["metadata"]?.DeepClone()
    };
    await Guardar("modelos.json", informe);
    return 0;
}

// ---------------------------------------------------------------------------------------------------------------

// Mezcla: la coleccion no recuerda su modelo; el 409 de 1.4 solo compara dimensiones. Un paso por proceso: una segunda
// WebApplicationFactory en el mismo proceso responde 503 a todo (artefacto del banco, ver README).
async Task<int> Mezcla(string paso)
{
    const string mezcla = ServicioEnProceso.Prefijo + "mezcla";
    await Registrar(mezcla);
    var pasos = new Dictionary<string, (PerfilEmbeddings Perfil, (string, string)? Doc)>
    {
        ["1"] = (PerfilEmbeddings.TeiArctic(TeiUrl), ("a.md", "# Facturas\n\nEl sistema emite una factura cuando el pedido se confirma y la envia al cliente por correo electronico.")),
        ["2"] = (PerfilEmbeddings.AltheiaBgeM3(256), ("b.md", "# Inventario\n\nCada salida de almacen descuenta existencias y genera una alerta si el producto queda por debajo del minimo.")),
        ["3"] = (PerfilEmbeddings.AltheiaBgeM3(1024), ("c.md", "# Nomina\n\nEl calculo de la nomina aplica las percepciones y deducciones vigentes en la fecha de pago de cada empleado.")),
        ["4"] = (PerfilEmbeddings.TeiArctic(TeiUrl), null),
    };
    if (paso == "1") await qdrant.DeleteAsync($"/collections/{mezcla}");
    var (perfil, doc) = pasos[paso];
    using var s = new ServicioEnProceso(QdrantHost, QdrantGrpc, perfil);
    using var http = s.Cliente();
    JsonObject fila;
    if (doc is { } d)
    {
        var (status, r, seg) = await Post(http, $"/api/colecciones/{mezcla}/ingestas", CuerpoIngesta([d]));
        fila = new JsonObject { ["perfil"] = perfil.Nombre, ["modelo"] = perfil.Modelo, ["dimension"] = perfil.Dimension, ["documento"] = d.Item1, ["status"] = status, ["segundos"] = seg, ["respuesta"] = r?.DeepClone() };
    }
    else
    {
        var (status, r, _) = await Post(http, $"/api/colecciones/{mezcla}/busquedas", new { consulta = "alerta de existencias minimas en el almacen", topK = 5 });
        fila = new JsonObject
        {
            ["perfil"] = perfil.Nombre, ["consulta"] = "alerta de existencias minimas en el almacen (el texto de b.md)", ["status"] = status,
            ["resultados"] = new JsonArray((r is JsonObject o && o["resultados"] is JsonArray a ? a : []).Select(x => (JsonNode)new JsonObject { ["ruta"] = (string?)x!["ruta"], ["puntuacion"] = (double?)x["puntuacion"] }).ToArray())
        };
    }
    fila["puntos_en_coleccion"] = await Puntos(mezcla);
    await Guardar($"mezcla-{paso}.json", fila);
    return 0;
}

// Cuantos chunks indexables produce un checkout (sin vectorizar), por lenguaje y por tamano estimado.
async Task<int> Chunks(string repo)
{
    using var servicio = new ServicioEnProceso(QdrantHost, QdrantGrpc, PerfilEmbeddings.TeiArctic(TeiUrl));
    servicio.Cliente().Dispose();
    var router = servicio.Services.GetRequiredService<ChunkingStrategyRouter>();
    var opciones = new ChunkingOptions { MaxTokensPerChunk = 1024, OverlapTokens = 64, RepositoryName = ServicioEnProceso.Prefijo + repo, BatchSize = 32 };
    var porLenguaje = new Dictionary<string, (int Chunks, long Caracteres)>();
    foreach (var a in EscanerDeCheckout.Escanear(Path.Combine(clones, repo), PerfilEscaneo.DotNetEnterprise))
    {
        var contenido = await File.ReadAllTextAsync(a.RutaAbsoluta);
        var artefacto = DocumentArtifacts.TryCreate(a.RutaRelativa.Replace('\\', '/'), contenido, ScanProfile.DotNetEnterprise, DateTimeOffset.UnixEpoch);
        if (artefacto is null) continue;
        await foreach (var ch in router.GetStrategy(artefacto).ChunkAsync(artefacto, contenido, opciones, CancellationToken.None))
        {
            if (ch.Content.AsSpan().Trim().Length < 60) continue;
            var k = artefacto.Language.ToString();
            var v = porLenguaje.GetValueOrDefault(k);
            porLenguaje[k] = (v.Chunks + 1, v.Caracteres + ch.EnrichedContent.Length);
        }
    }
    await Guardar($"chunks-{repo}.json", new JsonObject(porLenguaje.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)new JsonObject
    {
        ["chunks"] = kv.Value.Chunks, ["caracteres_enriquecidos"] = kv.Value.Caracteres
    }))) { ["total"] = porLenguaje.Values.Sum(v => v.Chunks) });
    return 0;
}

async Task<int> Sonda()
{
  for (var vuelta = 0; vuelta < 2; vuelta++)
  {
    using var s = new ServicioEnProceso(QdrantHost, QdrantGrpc, PerfilEmbeddings.TeiArctic(TeiUrl));
    using var http = s.Cliente();
    Console.WriteLine($"vuelta {vuelta} base {http.BaseAddress}");
    foreach (var (m, ruta, cuerpo) in new[] { ("GET", "/api/colecciones", (object?)null), ("POST", $"/api/colecciones/{ServicioEnProceso.Prefijo}mezcla/busquedas", new { consulta = "hola", topK = 1 }) })
    {
        using var r = m == "GET" ? await http.GetAsync(ruta) : await http.PostAsJsonAsync(ruta, cuerpo);
        Console.WriteLine($"{m} {ruta} -> {(int)r.StatusCode}");
        foreach (var h in r.Headers.Concat(r.Content.Headers)) Console.WriteLine($"  {h.Key}: {string.Join(",", h.Value)}");
        var t = await r.Content.ReadAsStringAsync();
        Console.WriteLine("  " + (t.Length > 120 ? t[..120] : t).Replace("\n", " "));
    }
  }
    return 0;
}

async Task<int> Limpiar()
{
    if (!File.Exists(registro)) { Console.WriteLine("Nada que borrar."); return 0; }
    foreach (var c in (await File.ReadAllLinesAsync(registro)).Where(l => l.Length > 0).Distinct())
    {
        if (!c.StartsWith(ServicioEnProceso.Prefijo, StringComparison.Ordinal)) throw new InvalidOperationException($"Nombre ajeno en el registro: {c}");
        var r = await qdrant.DeleteAsync($"/collections/{c}");
        var existe = JsonNode.Parse(await (await qdrant.GetAsync($"/collections/{c}/exists")).Content.ReadAsStringAsync())!["result"]!["exists"]!;
        Console.WriteLine($"{c}: delete {(int)r.StatusCode}, existe={existe}");
    }
    File.Delete(registro);
    return 0;
}
