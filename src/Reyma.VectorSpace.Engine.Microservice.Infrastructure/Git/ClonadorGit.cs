using System.Diagnostics;
using System.Text;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Git;

public sealed record OpcionesClonado
{
    public string Git { get; init; } = "git";

    public int Profundidad { get; init; } = 1;

    public long MaxBytes { get; init; } = 200L * 1024 * 1024;

    public TimeSpan Plazo { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Cabecera <c>Authorization</c> completa (p. ej. <c>Basic &lt;base64&gt;</c> para un PAT de Azure DevOps) de la
    /// identidad del SERVICIO frente al host. Viaja por GIT_CONFIG_KEY/VALUE en el entorno del proceso: ni en la
    /// linea de comandos (visible en la lista de procesos) ni en .git/config del checkout. Si es null, el clonado NO
    /// aisla la configuracion/credenciales del host: deja que su <c>credential.helper</c> ya configurado resuelva la
    /// autenticacion. Eso es intencional SOLO para la verificacion puntual de 1.7.6 (credencial personal ya cacheada
    /// del usuario en este equipo); produccion siempre configura esta identidad por variable de entorno
    /// (<see cref="GitOptions.NombreVariableIdentidad"/>) y nunca deja caer en esta rama.
    /// </summary>
    public string? CabeceraAutorizacion { get; init; }
}

public sealed record ResultadoClonado(
    bool Exito, string? Motivo, int? CodigoSalida, long Bytes, double Segundos, string Stderr);

/// <summary>
/// Portado de tools/ingesta-proyecto/Prototipo/ClonadorGit.cs (1.7.3), con una diferencia de diseno deliberada
/// (ver <see cref="OpcionesClonado.CabeceraAutorizacion"/>): el prototipo SIEMPRE aislaba la configuracion/credenciales
/// del host (para medir el clonado endurecido de forma reproducible); esta version solo aisla cuando hay una
/// identidad explicita que inyectar, y si no la hay, deja que el credential helper del host resuelva la autenticacion
/// en vez de fallar exigiendo una identidad que todavia no existe (decision del usuario para la verificacion de
/// 1.7.6, documentada en el ledger, item 1.7.6, "_reentrada_resuelta_2026_10_23").
///
/// Clonado superficial (una revision, una rama, sin tags ni submodulos), sin redirecciones (SSRF, 1.7.3: git con
/// <c>http.followRedirects=initial</c> sigue una redireccion del host permitido hacia un host interno y clona desde
/// ahi), solo https/http segun <paramref name="permitirHttp"/>, sin hooks ni enlaces simbolicos. Tamano y plazo se
/// vigilan desde fuera y el proceso se mata al rebasarlos (tope de proceso, no de filesystem: ver GitOptions.MaxBytesCheckout).
/// </summary>
public static class ClonadorGit
{
    public static async Task<ResultadoClonado> ClonarAsync(
        Uri uri, string destino, OpcionesClonado o, bool permitirHttp = false, CancellationToken ct = default)
    {
        if (Directory.Exists(destino) && Directory.EnumerateFileSystemEntries(destino).Any())
            throw new InvalidOperationException($"El destino no esta vacio: {destino}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destino))!);

        var argumentos = new List<string>
        {
            "-c", "protocol.allow=never",
            "-c", "protocol.https.allow=always",
            "-c", $"protocol.http.allow={(permitirHttp ? "always" : "never")}",
            "-c", "http.followRedirects=false",
            "-c", "core.symlinks=false",
            "-c", "core.fsmonitor=false",
            "-c", "submodule.recurse=false",
            "-c", "http.lowSpeedLimit=1024",
            "-c", "http.lowSpeedTime=30",
        };
        if (OperatingSystem.IsWindows()) argumentos.InsertRange(0, ["-c", "core.longpaths=true"]);

        string? configVacia = null;
        if (o.CabeceraAutorizacion is not null)
        {
            // Identidad explicita: aislar config/credenciales del host (credential.helper del sistema NO debe
            // presentar las credenciales de quien corre el proceso ante un 401).
            var vacio = Path.Combine(Path.GetTempPath(), "vse-git-vacio");
            Directory.CreateDirectory(vacio);
            configVacia = Path.Combine(vacio, "gitconfig");
            if (!File.Exists(configVacia)) await File.WriteAllTextAsync(configVacia, "", ct);
            var hooksVacios = Path.Combine(vacio, "hooks");
            Directory.CreateDirectory(hooksVacios);
            argumentos.AddRange(["-c", "credential.helper=", "-c", "core.askPass=", "-c", $"core.hooksPath={hooksVacios}"]);
        }

        argumentos.AddRange(["clone", "--depth", o.Profundidad.ToString(), "--single-branch", "--no-tags", "--no-recurse-submodules",
            "--", uri.AbsoluteUri, destino]);

        var psi = new ProcessStartInfo(o.Git)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var a in argumentos) psi.ArgumentList.Add(a);

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
        if (o.CabeceraAutorizacion is not null)
        {
            // Nada heredado de quien lanza el proceso: ni GIT_* (un GIT_DIR o GIT_SSH_COMMAND colado cambia que hace
            // git) ni su configuracion global o del sistema ni GCM.
            foreach (var clave in psi.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)
                         || k.StartsWith("GCM_", StringComparison.OrdinalIgnoreCase)).ToList())
                psi.Environment.Remove(clave);
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GCM_INTERACTIVE"] = "never";
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            psi.Environment["GIT_CONFIG_GLOBAL"] = configVacia!;
            psi.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
            psi.Environment["GIT_CONFIG_COUNT"] = "1";
            psi.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            psi.Environment["GIT_CONFIG_VALUE_0"] = "Authorization: " + o.CabeceraAutorizacion;
        }
        // Sin CabeceraAutorizacion: se deja GIT_TERMINAL_PROMPT/GIT_LFS_SKIP_SMUDGE puestos arriba y todo lo demas del
        // entorno/config del host intacto, para que su credential.helper (Windows Git Credential Manager en esta
        // verificacion) pueda resolver la autenticacion sin preguntar interactivamente.

        var sw = Stopwatch.StartNew();
        using var proceso = Process.Start(psi)!;
        proceso.StandardInput.Close();
        var stderr = new StringBuilder();
        var leerErr = Task.Run(async () =>
        {
            string? linea;
            while ((linea = await proceso.StandardError.ReadLineAsync()) is not null)
                lock (stderr) if (stderr.Length < 8_000) stderr.AppendLine(linea);
        });
        var leerOut = proceso.StandardOutput.ReadToEndAsync();

        string? motivo = null;
        long bytes = 0;
        while (!proceso.HasExited)
        {
            await Task.WhenAny(proceso.WaitForExitAsync(ct), Task.Delay(250, ct));
            bytes = TamanoDirectorio(destino);
            if (bytes > o.MaxBytes) motivo = $"El repositorio supera el maximo de {o.MaxBytes} bytes.";
            else if (sw.Elapsed > o.Plazo) motivo = $"El clonado supero el plazo de {o.Plazo.TotalSeconds:0} s.";
            if (motivo is not null)
            {
                try { proceso.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                break;
            }
        }
        await proceso.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(leerErr, leerOut);
        sw.Stop();
        bytes = TamanoDirectorio(destino);

        var exito = motivo is null && proceso.ExitCode == 0;
        if (!exito && motivo is null) motivo = "git termino con error.";
        return new ResultadoClonado(exito, motivo, proceso.ExitCode, bytes, Math.Round(sw.Elapsed.TotalSeconds, 2),
            stderr.ToString().Trim());
    }

    public static long TamanoDirectorio(string ruta)
    {
        if (!Directory.Exists(ruta)) return 0;
        long total = 0;
        try
        {
            foreach (var f in new DirectoryInfo(ruta).EnumerateFiles("*", new EnumerationOptions
                     { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                try { total += f.Length; } catch (IOException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return total;
    }

    /// <summary>
    /// Borra un checkout temporal; git marca los objetos de solo lectura en Windows. Reintenta un par de veces con
    /// espera corta: medido en 1.7.6, justo despues de que el proceso de git termina, Windows puede tardar un
    /// instante en soltar el handle de algun archivo (antivirus/indexador), y el primer intento de borrado falla con
    /// <see cref="IOException"/> aunque nada siga usando el directorio.
    /// </summary>
    public static void Borrar(string ruta)
    {
        if (!Directory.Exists(ruta)) return;
        foreach (var f in new DirectoryInfo(ruta).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            try { f.Attributes = FileAttributes.Normal; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        for (var intento = 1; ; intento++)
        {
            try
            {
                Directory.Delete(ruta, recursive: true);
                return;
            }
            catch (IOException) when (intento < 3)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (intento < 3)
            {
                Thread.Sleep(200);
            }
        }
    }
}
