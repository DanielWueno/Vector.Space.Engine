using System.Diagnostics;
using System.Text;

namespace IngestaProyecto.Prototipo;

public sealed record OpcionesClonado
{
    public string Git { get; init; } = "git";
    public int Profundidad { get; init; } = 1;
    public string? Rama { get; init; }
    public long MaxBytes { get; init; } = 200L * 1024 * 1024;
    public TimeSpan Plazo { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Solo para el banco: el origen de prueba es http sobre loopback. En el servicio, https y nada mas.</summary>
    public bool PermitirHttp { get; init; }

    /// <summary>Solo para medir la linea base: git sigue por defecto la redireccion de la peticion inicial.</summary>
    public bool SeguirRedireccionesComoGitPorDefecto { get; init; }

    /// <summary>
    /// Cabecera <c>Authorization</c> de la identidad del SERVICIO frente al host (PAT o token de Entra ID de una cuenta
    /// no personal). Viaja por GIT_CONFIG_KEY/VALUE en el entorno del proceso: ni en la linea de comandos (visible en
    /// la lista de procesos) ni en .git/config del checkout.
    /// </summary>
    public string? CabeceraAutorizacion { get; init; }
}

public sealed record ResultadoClonado(
    bool Exito, string? Motivo, int? CodigoSalida, long Bytes, double Segundos, string Stderr, IReadOnlyList<string> Argumentos);

/// <summary>
/// Clonado endurecido con el git del sistema: superficial (una revision, una rama, sin tags ni submodulos), sin
/// redirecciones, solo https, sin hooks ni enlaces simbolicos, y aislado de la configuracion y las credenciales de quien
/// corre el proceso (en esta maquina el gitconfig del sistema trae credential.helper=manager: sin aislar, un 401 haria
/// que Git Credential Manager presentara las credenciales del usuario). Tamano y plazo se vigilan desde fuera y el
/// proceso se mata al rebasarlos.
/// </summary>
public static class ClonadorGit
{
    public static async Task<ResultadoClonado> ClonarAsync(Uri uri, string destino, OpcionesClonado o, CancellationToken ct = default)
    {
        if (Directory.Exists(destino) && Directory.EnumerateFileSystemEntries(destino).Any())
            throw new InvalidOperationException($"El destino no esta vacio: {destino}");

        var vacio = Path.Combine(Path.GetTempPath(), "vse-git-vacio");
        Directory.CreateDirectory(vacio);
        var configVacia = Path.Combine(vacio, "gitconfig");
        if (!File.Exists(configVacia)) await File.WriteAllTextAsync(configVacia, "", ct);
        var hooksVacios = Path.Combine(vacio, "hooks");
        Directory.CreateDirectory(hooksVacios);

        var argumentos = new List<string>
        {
            "-c", "protocol.allow=never",
            "-c", "protocol.https.allow=always",
            "-c", $"protocol.http.allow={(o.PermitirHttp ? "always" : "never")}",
            "-c", $"http.followRedirects={(o.SeguirRedireccionesComoGitPorDefecto ? "initial" : "false")}",
            "-c", "credential.helper=",
            "-c", "core.askPass=",
            "-c", "core.symlinks=false",
            "-c", $"core.hooksPath={hooksVacios}",
            "-c", "core.fsmonitor=false",
            "-c", "submodule.recurse=false",
            "-c", "http.lowSpeedLimit=1024",
            "-c", "http.lowSpeedTime=30",
            "clone", "--depth", o.Profundidad.ToString(), "--single-branch", "--no-tags", "--no-recurse-submodules",
        };
        if (OperatingSystem.IsWindows()) argumentos.InsertRange(0, ["-c", "core.longpaths=true"]);
        if (o.Rama is not null) argumentos.AddRange(["--branch", o.Rama]);
        argumentos.AddRange(["--", uri.AbsoluteUri, destino]);

        var psi = new ProcessStartInfo(o.Git)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var a in argumentos) psi.ArgumentList.Add(a);

        // Nada heredado de quien lanza el proceso: ni GIT_* (un GIT_DIR o GIT_SSH_COMMAND colado cambia que hace git)
        // ni su configuracion global o del sistema.
        foreach (var clave in psi.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)
                     || k.StartsWith("GCM_", StringComparison.OrdinalIgnoreCase)).ToList())
            psi.Environment.Remove(clave);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["GIT_CONFIG_GLOBAL"] = configVacia;
        psi.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
        if (o.CabeceraAutorizacion is not null)
        {
            psi.Environment["GIT_CONFIG_COUNT"] = "1";
            psi.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            psi.Environment["GIT_CONFIG_VALUE_0"] = "Authorization: " + o.CabeceraAutorizacion;
        }

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
            stderr.ToString().Trim(), argumentos);
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

    /// <summary>Borra un checkout temporal; git marca los objetos de solo lectura en Windows.</summary>
    public static void Borrar(string ruta)
    {
        if (!Directory.Exists(ruta)) return;
        foreach (var f in new DirectoryInfo(ruta).EnumerateFiles("*", SearchOption.AllDirectories))
            f.Attributes = FileAttributes.Normal;
        Directory.Delete(ruta, recursive: true);
    }
}
