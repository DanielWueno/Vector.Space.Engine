using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IngestaProyecto.Banco;

/// <summary>
/// Host Git de prueba sobre loopback: el protocolo smart HTTP real (git-http-backend por CGI, el mismo binario que usan
/// los servidores Git) sobre copias bare de repos propios. Hace de "host de la lista blanca" sin credenciales de Azure
/// DevOps. Rutas:
///   /git/...       publico
///   /privado/...   exige Authorization: Basic con la identidad de prueba del SERVICIO (generada al arrancar)
///   /redirige/...  302 hacia el servidor "interno" (otro puerto, fuera de la lista blanca)
///   /lento/...     espera 120 s antes de responder
/// El servidor interno sirve los mismos repos y cuenta las peticiones que le llegan: si una redireccion se sigue, se nota.
/// </summary>
public sealed class ServidorGitDePrueba : IAsyncDisposable
{
    private readonly WebApplication _publico;
    private readonly WebApplication _interno;
    private int _peticionesInternas;

    public int Puerto { get; }
    public int PuertoInterno { get; }
    public string UsuarioServicio => "vse-servicio";
    public string ClaveServicio { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    public int PeticionesInternas => Volatile.Read(ref _peticionesInternas);
    public string CabeceraServicio => "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"{UsuarioServicio}:{ClaveServicio}"));

    private ServidorGitDePrueba(WebApplication publico, WebApplication interno, int puerto, int puertoInterno)
    {
        _publico = publico; _interno = interno; Puerto = puerto; PuertoInterno = puertoInterno;
    }

    public static async Task<ServidorGitDePrueba> IniciarAsync(string raizRepos, string httpBackend)
    {
        var interno = Construir();
        ServidorGitDePrueba? yo = null;
        interno.Map("/{**ruta}", async ctx =>
        {
            Interlocked.Increment(ref yo!._peticionesInternas);
            var ruta = ctx.Request.Path.Value!;
            await Cgi(ctx, raizRepos, httpBackend, ruta.StartsWith("/git/", StringComparison.Ordinal) ? ruta["/git".Length..] : ruta);
        });
        await interno.StartAsync();

        var publico = Construir();
        publico.Map("/git/{**ruta}", ctx => Cgi(ctx, raizRepos, httpBackend, ctx.Request.Path.Value!["/git".Length..]));
        publico.Map("/privado/{**ruta}", async ctx =>
        {
            if (ctx.Request.Headers.Authorization.ToString() != yo!.CabeceraServicio)
            {
                ctx.Response.StatusCode = 401;
                ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"vse-prueba\"";
                return;
            }
            await Cgi(ctx, raizRepos, httpBackend, ctx.Request.Path.Value!["/privado".Length..]);
        });
        publico.Map("/redirige/{**ruta}", ctx =>
        {
            ctx.Response.Redirect($"http://127.0.0.1:{yo!.PuertoInterno}/git{ctx.Request.Path.Value!["/redirige".Length..]}{ctx.Request.QueryString}");
            return Task.CompletedTask;
        });
        publico.Map("/lento/{**ruta}", async ctx =>
        {
            await Task.Delay(TimeSpan.FromSeconds(120), ctx.RequestAborted);
            await Cgi(ctx, raizRepos, httpBackend, ctx.Request.Path.Value!["/lento".Length..]);
        });
        await publico.StartAsync();

        yo = new ServidorGitDePrueba(publico, interno, PuertoDe(publico), PuertoDe(interno));
        return yo;
    }

    private static WebApplication Construir()
    {
        var b = WebApplication.CreateSlimBuilder();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        b.Logging.ClearProviders();
        return b.Build();
    }

    private static int PuertoDe(WebApplication app) =>
        new Uri(app.Urls.First()).Port;

    /// <summary>CGI minimo para git-http-backend: entorno, cuerpo por stdin y cabeceras CGI de vuelta.</summary>
    private static async Task Cgi(HttpContext ctx, string raiz, string backend, string pathInfo)
    {
        var psi = new ProcessStartInfo(backend)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        psi.Environment["GIT_PROJECT_ROOT"] = raiz;
        psi.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
        psi.Environment["PATH_INFO"] = pathInfo;
        psi.Environment["REQUEST_METHOD"] = ctx.Request.Method;
        psi.Environment["QUERY_STRING"] = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value![1..] : "";
        psi.Environment["CONTENT_TYPE"] = ctx.Request.ContentType ?? "";
        psi.Environment["REMOTE_ADDR"] = "127.0.0.1";
        psi.Environment["SERVER_PROTOCOL"] = "HTTP/1.1";
        psi.Environment["GATEWAY_INTERFACE"] = "CGI/1.1";
        if (ctx.Request.Headers.TryGetValue("Content-Encoding", out var ce)) psi.Environment["HTTP_CONTENT_ENCODING"] = ce.ToString();
        if (ctx.Request.Headers.TryGetValue("Git-Protocol", out var gp)) psi.Environment["GIT_PROTOCOL"] = gp.ToString();
        if (ctx.Request.ContentLength is long n) psi.Environment["CONTENT_LENGTH"] = n.ToString();

        using var p = Process.Start(psi)!;
        var entrada = Task.Run(async () =>
        {
            try { await ctx.Request.Body.CopyToAsync(p.StandardInput.BaseStream); }
            finally { p.StandardInput.Close(); }
        });
        var errores = p.StandardError.ReadToEndAsync();
        var salida = p.StandardOutput.BaseStream;

        // Cabeceras CGI hasta la linea vacia.
        var cabecera = new MemoryStream();
        var uno = new byte[1];
        while (await salida.ReadAsync(uno) == 1)
        {
            cabecera.WriteByte(uno[0]);
            var b = cabecera.GetBuffer();
            var l = (int)cabecera.Length;
            if (l >= 2 && b[l - 1] == '\n' && (b[l - 2] == '\n' || (l >= 3 && b[l - 2] == '\r' && b[l - 3] == '\n'))) break;
        }
        foreach (var linea in Encoding.ASCII.GetString(cabecera.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = linea.IndexOf(':');
            if (i <= 0) continue;
            var (k, v) = (linea[..i].Trim(), linea[(i + 1)..].Trim());
            if (k.Equals("Status", StringComparison.OrdinalIgnoreCase)) ctx.Response.StatusCode = int.Parse(v.Split(' ')[0]);
            else ctx.Response.Headers[k] = v;
        }
        await salida.CopyToAsync(ctx.Response.Body);
        await entrada;
        await errores;
        await p.WaitForExitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _publico.DisposeAsync();
        await _interno.DisposeAsync();
    }
}
