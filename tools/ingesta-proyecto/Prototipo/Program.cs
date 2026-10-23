// 1.7.3: pruebas de los prototipos que no necesitan red ni servicio. Ver ../README.md.
//
//   urls                         lista blanca: casos permitidos y rechazados (sin tocar la red)
//   zip <trabajo> [<valido.zip>]  zips validos y maliciosos: lector en memoria, extraccion ingenua y ZipFile.ExtractToDirectory
//   enlaces <trabajo>             escaner portado frente a un enlace simbolico / junction que apunta fuera de la raiz
//   escaner <raiz> <salida.json>  rutas que emite el escaner portado (para compararlo con el original desde el Banco)
//
// Corre igual en Windows y en Linux (mcr.microsoft.com/dotnet/sdk:10.0): solo BCL.
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using IngestaProyecto.Prototipo;

var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var plataforma = $"{RuntimeInformation.OSDescription} / {RuntimeInformation.FrameworkDescription}";

if (args.Length == 0) { Console.Error.WriteLine("Uso: urls | zip <trabajo> [<valido.zip>] | enlaces <trabajo> | escaner <raiz> <salida.json>"); return 2; }
switch (args[0])
{
    case "urls": Console.WriteLine(Urls().ToJsonString(json)); return 0;
    case "zip": Console.WriteLine(Zips(args[1], args.Length > 2 ? args[2] : null).ToJsonString(json)); return 0;
    case "enlaces": Console.WriteLine(Enlaces(args[1]).ToJsonString(json)); return 0;
    case "ragignore": Console.WriteLine(Ragignore(args[1]).ToJsonString(json)); return 0;
    case "escaner":
        {
            var sw = Stopwatch.StartNew();
            var archivos = EscanerDeCheckout.Escanear(args[1], PerfilEscaneo.DotNetEnterprise);
            var informe = new JsonObject
            {
                ["plataforma"] = plataforma, ["segundos"] = Math.Round(sw.Elapsed.TotalSeconds, 3), ["archivos"] = archivos.Count,
                ["rutas"] = new JsonArray(archivos.Select(a => (JsonNode)a.RutaRelativa.Replace('\\', '/')).ToArray())
            };
            File.WriteAllText(args[2], informe.ToJsonString(json));
            Console.WriteLine($"{archivos.Count} archivos en {sw.Elapsed.TotalSeconds:0.000} s");
            return 0;
        }
    default: Console.Error.WriteLine($"Comando desconocido: {args[0]}"); return 2;
}

JsonObject Urls()
{
    // Lo que tendria el servicio: solo la organizacion de Azure DevOps de la empresa (nombre ficticio aqui).
    OrigenGitPermitido[] produccion =
    [
        new("https", "dev.azure.com", 443, "/organizacion-ejemplo/"),
        new("https", "organizacion-ejemplo.visualstudio.com", 443),
    ];
    string[] casos =
    [
        "https://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo",
        "https://dev.azure.com/organizacion-ejemplo/Mi%20Proyecto/_git/repo",
        "https://organizacion-ejemplo.visualstudio.com/Proyecto/_git/repo",
        "https://dev.azure.com/otra-organizacion/Proyecto/_git/repo",
        "https://dev.azure.com/organizacion-ejemplo/../otra-organizacion/_git/repo",
        "https://dev.azure.com/organizacion-ejemplo/%2e%2e/otra-organizacion/_git/repo",
        "https://dev.azure.com.atacante.example/organizacion-ejemplo/_git/repo",
        "https://dev.azure.com@atacante.example/organizacion-ejemplo/_git/repo",
        "https://atacante.example#@dev.azure.com/organizacion-ejemplo/",
        "https://usuario:secreto@dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo",
        "http://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo",
        "https://dev.azure.com:8443/organizacion-ejemplo/Proyecto/_git/repo",
        "https://github.com/DanielWueno/Rag.Context.Engine.git",
        "https://169.254.169.254/latest/meta-data/",
        "http://127.0.0.1:6333/collections",
        "https://localhost/organizacion-ejemplo/",
        "file:///etc/passwd",
        "ssh://git@ssh.dev.azure.com/v3/organizacion-ejemplo/Proyecto/repo",
        "git@ssh.dev.azure.com:v3/organizacion-ejemplo/Proyecto/repo",
        "ext::sh -c touch% /tmp/pwned",
        "--upload-pack=touch /tmp/pwned",
        "https://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo?version=GBmain",
        "https://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo\n-c core.sshCommand=x",
    ];
    var filas = new JsonArray();
    foreach (var c in casos)
    {
        var (uri, error) = ListaBlancaGit.Validar(c, produccion);
        filas.Add(new JsonObject { ["url"] = c, ["admitida"] = uri is not null, ["motivo"] = error });
    }
    return new JsonObject
    {
        ["plataforma"] = plataforma,
        ["lista_blanca"] = new JsonArray(produccion.Select(p => (JsonNode)$"{p.Esquema}://{p.Host}:{p.Puerto}{p.PrefijoRuta}").ToArray()),
        ["admitidas"] = filas.Count(f => (bool)f!["admitida"]!), ["rechazadas"] = filas.Count(f => !(bool)f!["admitida"]!),
        ["casos"] = filas
    };
}

JsonObject Zips(string trabajo, string? valido)
{
    Directory.CreateDirectory(trabajo);
    var perfil = PerfilEscaneo.DotNetEnterprise;
    var limites = new LimitesZip();
    var informe = new JsonObject { ["plataforma"] = plataforma, ["limites"] = JsonSerializer.SerializeToNode(limites) };
    var casos = new JsonArray();

    var sinRatio = limites with { MaxRatio = double.MaxValue };
    var archivos = new List<(string Nombre, string Ruta, string Descripcion, string? Raiz, LimitesZip Limites)>();
    if (valido is not null)
    {
        archivos.Add(("valido", valido, "git archive --format=zip --prefix=<repo>/ de un repo real, con carpetaRaiz", Path.GetFileNameWithoutExtension(valido).Replace("valido-", "") switch { "vse" => "Vector.Space.Engine", var x => x }, limites));
        archivos.Add(("valido-sin-carpeta-raiz", valido, "el mismo zip sin indicar carpetaRaiz: las rutas conservan el prefijo", null, limites));
    }
    var slip = Path.Combine(trabajo, "slip.zip"); ZipsDePrueba.Slip(slip);
    archivos.Add(("slip", slip, "Zip Slip: ../, ..\\, absolutas, unidad, flujo alterno", null, limites));
    var bomba = Path.Combine(trabajo, "bomba.zip"); ZipsDePrueba.Bomba(bomba, 1L << 30);
    archivos.Add(("bomba", bomba, "1 GiB de ceros en una entrada .cs", null, limites));
    var chica = Path.Combine(trabajo, "bomba-chica.zip"); ZipsDePrueba.BombaDeEntradas(chica, 1, 500_000);
    archivos.Add(("bomba-chica", chica, "una entrada .md de 500 000 ceros (bajo el tope por archivo)", null, limites));
    var bombaEntradas = Path.Combine(trabajo, "bomba-entradas.zip"); ZipsDePrueba.BombaDeEntradas(bombaEntradas, 1_000, 500_000);
    archivos.Add(("bomba-entradas", bombaEntradas, "1000 entradas .md de 500 000 ceros (~477 MiB)", null, limites));
    archivos.Add(("bomba-entradas-sin-ratio", bombaEntradas, "la misma, con el chequeo de ratio desactivado: tiene que pararla el tope total", null, sinRatio));
    var miente = Path.Combine(trabajo, "miente.zip"); ZipsDePrueba.Mentirosa(miente, 50L * 1024 * 1024);
    archivos.Add(("miente", miente, "50 MiB reales con cabeceras que declaran 100 bytes", null, limites));
    var muchas = Path.Combine(trabajo, "muchas.zip"); ZipsDePrueba.MuchasEntradas(muchas, 30_000);
    archivos.Add(("muchas", muchas, "30 000 entradas vacias", null, limites));
    var enlace = Path.Combine(trabajo, "enlace.zip"); ZipsDePrueba.Enlace(enlace);
    archivos.Add(("enlace", enlace, "entrada S_IFLNK que apunta fuera", null, limites));
    var grande = Path.Combine(trabajo, "grande.zip");
    using (var f = File.Create(grande)) f.SetLength(limites.MaxBytesZip + 1);
    archivos.Add(("grande", grande, "un archivo de MaxBytesZip + 1 bytes", null, limites));

    foreach (var (nombre, ruta, descripcion, raiz, lim) in archivos)
    {
        var caso = new JsonObject { ["caso"] = nombre, ["descripcion"] = descripcion, ["bytes_zip"] = new FileInfo(ruta).Length, ["carpeta_raiz"] = raiz };

        // 1) Prototipo: documentos en memoria.
        var sw = Stopwatch.StartNew();
        var antes = GC.GetTotalAllocatedBytes(precise: true);
        ResultadoZip r;
        using (var f = File.OpenRead(ruta)) r = LectorZip.Leer(f, perfil, lim, raiz);
        caso["lector_en_memoria"] = new JsonObject
        {
            ["error"] = r.Error, ["entradas"] = r.Entradas, ["documentos"] = r.Documentos.Count, ["fuera_de_perfil"] = r.FueraDePerfil,
            ["bytes_leidos"] = r.BytesLeidos, ["prefijo_quitado"] = r.PrefijoQuitado,
            ["rechazadas"] = new JsonArray(r.Rechazadas.Select(x => (JsonNode)new JsonObject { ["entrada"] = x.Entrada, ["motivo"] = x.Motivo }).ToArray()),
            ["segundos"] = Math.Round(sw.Elapsed.TotalSeconds, 3),
            ["mb_asignados"] = Math.Round((GC.GetTotalAllocatedBytes(precise: true) - antes) / 1048576d, 1),
            ["rutas_muestra"] = new JsonArray(r.Documentos.Take(5).Select(d => (JsonNode)d.Ruta).ToArray())
        };

        if (nombre is "slip" or "enlace")
        {
            // 2) Extraccion ingenua dentro de un cajon: <cajon>/a/b/c/destino.
            var cajon = Path.Combine(trabajo, $"cajon-{nombre}");
            if (Directory.Exists(cajon)) Directory.Delete(cajon, true);
            var destino = Path.Combine(cajon, "a", "b", "c", "destino");
            Directory.CreateDirectory(destino);
            using (var f = File.OpenRead(ruta))
            {
                var fuera = LectorZip.ExtraerIngenuo(f, destino, cajon);
                caso["extraccion_ingenua"] = new JsonArray(fuera.Select(x => (JsonNode)new JsonObject
                {
                    ["entrada"] = x.Entrada, ["destino"] = Path.GetRelativePath(cajon, x.Destino), ["escrito"] = x.Escrito
                }).ToArray());
            }

            // 3) ZipFile.ExtractToDirectory del BCL.
            var destino2 = Path.Combine(cajon, "bcl", "destino");
            Directory.CreateDirectory(destino2);
            string? errorBcl = null;
            try { ZipFile.ExtractToDirectory(ruta, destino2, overwriteFiles: true); }
            catch (Exception ex) { errorBcl = $"{ex.GetType().Name}: {ex.Message}"; }
            var escritos = Directory.EnumerateFiles(cajon, "*", SearchOption.AllDirectories)
                .Where(p => p.StartsWith(Path.Combine(cajon, "bcl"), StringComparison.Ordinal))
                .Select(p => new FileInfo(p))
                .Select(fi => (JsonNode)new JsonObject
                {
                    ["archivo"] = Path.GetRelativePath(destino2, fi.FullName), ["enlace_a"] = fi.LinkTarget
                }).ToArray();
            caso["extract_to_directory"] = new JsonObject { ["error"] = errorBcl, ["escritos_bajo_bcl"] = new JsonArray(escritos) };
        }

        casos.Add(caso);
    }
    informe["casos"] = casos;
    return informe;
}

// .gitignore + .ragignore propios del proyecto: el mismo arbol como checkout (escaner portado) y como .zip (lector).
JsonObject Ragignore(string trabajo)
{
    var raiz = Path.Combine(trabajo, "proyecto");
    if (Directory.Exists(trabajo)) Directory.Delete(trabajo, true);
    var relleno = "\n\nTexto suficiente para pasar el minimo de sesenta caracteres que exige el pipeline para indexar.\n";
    var archivos = new Dictionary<string, string>
    {
        [".gitignore"] = "bin/\n*.log\n",
        [".ragignore"] = "# exclusiones propias del corpus\ndocs/privado/\n!docs/privado/publico.md\n*.sql\n.env.json\nsrc/Generado/**\n",
        ["src/App.cs"] = "namespace Demo;\n\npublic sealed class App\n{\n    public int Sumar(int a, int b) => a + b; // " + relleno + "}\n",
        ["src/Generado/Cliente.g.cs"] = "// generado" + relleno,
        ["docs/guia.md"] = "# Guia" + relleno,
        ["docs/privado/secreto.md"] = "# Secreto" + relleno,
        ["docs/privado/publico.md"] = "# Publico" + relleno,
        ["db/esquema.sql"] = "create table t (id int);" + relleno,
        [".env.json"] = "{ \"clave\": \"valor-de-prueba-no-secreto\" }",
        ["bin/salida.json"] = "{ \"build\": true }",
        ["traza.log"] = "no es extension admitida",
    };
    foreach (var (ruta, texto) in archivos)
    {
        var completo = Path.Combine(raiz, ruta.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(completo)!);
        File.WriteAllText(completo, texto);
    }
    var checkout = EscanerDeCheckout.Escanear(raiz, PerfilEscaneo.DotNetEnterprise).Select(a => a.RutaRelativa.Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
    var zip = Path.Combine(trabajo, "proyecto.zip");
    ZipFile.CreateFromDirectory(raiz, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
    ResultadoZip r;
    using (var f = File.OpenRead(zip)) r = LectorZip.Leer(f, PerfilEscaneo.DotNetEnterprise, new LimitesZip(), "proyecto");
    var deZip = r.Documentos.Select(d => d.Ruta).Order(StringComparer.Ordinal).ToList();
    string[] esperado = ["docs/guia.md", "docs/privado/publico.md", "src/App.cs"];
    return new JsonObject
    {
        ["plataforma"] = plataforma,
        ["reglas_gitignore"] = archivos[".gitignore"], ["reglas_ragignore"] = archivos[".ragignore"],
        ["archivos"] = new JsonArray(archivos.Keys.Select(k => (JsonNode)k).ToArray()),
        ["esperado"] = new JsonArray(esperado.Select(e => (JsonNode)e).ToArray()),
        ["checkout_escaner_portado"] = new JsonArray(checkout.Select(x => (JsonNode)x).ToArray()),
        ["zip_lector"] = new JsonArray(deZip.Select(x => (JsonNode)x).ToArray()),
        ["checkout_igual_a_esperado"] = checkout.SequenceEqual(esperado),
        ["zip_igual_a_esperado"] = deZip.SequenceEqual(esperado)
    };
}

JsonObject Enlaces(string trabajo)
{
    var raiz = Path.Combine(trabajo, "repo");
    var fuera = Path.Combine(trabajo, "fuera-de-la-raiz");
    if (Directory.Exists(trabajo)) Directory.Delete(trabajo, true);
    Directory.CreateDirectory(Path.Combine(raiz, "docs"));
    Directory.CreateDirectory(fuera);
    File.WriteAllText(Path.Combine(raiz, "docs", "legitimo.md"), "# Legitimo\n\nDentro de la raiz.\n");
    File.WriteAllText(Path.Combine(fuera, "secreto.md"), "# Fuera\n\nNo deberia indexarse: vive fuera de la raiz del checkout.\n");

    var creados = new JsonArray();
    // Directorio: en Windows una junction (no pide privilegios); en Linux un symlink.
    var enlaceDir = Path.Combine(raiz, "enlace-dir");
    try
    {
        if (OperatingSystem.IsWindows())
        {
            var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{enlaceDir}\" \"{fuera}\"") { RedirectStandardOutput = true, UseShellExecute = false })!;
            p.WaitForExit();
            creados.Add(Directory.Exists(enlaceDir) ? "junction enlace-dir -> fuera-de-la-raiz" : "junction: no se pudo crear");
        }
        else
        {
            Directory.CreateSymbolicLink(enlaceDir, fuera);
            creados.Add("symlink enlace-dir -> fuera-de-la-raiz");
        }
    }
    catch (Exception ex) { creados.Add($"enlace de directorio: {ex.GetType().Name}"); }
    try
    {
        File.CreateSymbolicLink(Path.Combine(raiz, "docs", "enlace.md"), Path.Combine(fuera, "secreto.md"));
        creados.Add("symlink docs/enlace.md -> fuera-de-la-raiz/secreto.md");
    }
    catch (Exception ex) { creados.Add($"symlink de archivo: {ex.GetType().Name} (en Windows pide modo desarrollador o admin)"); }

    var omisiones = new List<EscanerDeCheckout.Omision>();
    var archivos = EscanerDeCheckout.Escanear(raiz, PerfilEscaneo.DotNetEnterprise, omisiones);
    return new JsonObject
    {
        ["plataforma"] = plataforma, ["raiz"] = raiz, ["enlaces_creados"] = creados,
        ["escaner_portado"] = new JsonObject
        {
            ["rutas"] = new JsonArray(archivos.Select(a => (JsonNode)a.RutaRelativa.Replace('\\', '/')).ToArray()),
            ["omitidos"] = new JsonArray(omisiones.Select(o => (JsonNode)$"{o.Ruta.Replace('\\', '/')}: {o.Motivo}").ToArray()),
            ["leyo_fuera_de_la_raiz"] = archivos.Any(a => !Path.GetFullPath(a.RutaAbsoluta).StartsWith(Path.GetFullPath(raiz), StringComparison.OrdinalIgnoreCase)
                || a.RutaRelativa.Contains("enlace"))
        }
    };
}
