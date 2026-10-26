using System.Net;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Git;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Infrastructure;

/// <summary>
/// Item 1.7.6, criterio literal de la ficha: "una redireccion hacia fuera se rechaza sin conexion al destino". Dos
/// listeners HTTP en loopback: uno responde 302 hacia el otro; <see cref="ClonadorGit"/> (igual que el prototipo de
/// 1.7.3, <c>http.followRedirects=false</c>) no debe seguirla, asi que el segundo listener nunca ve una peticion.
/// </summary>
public sealed class ClonadorGitTests
{
    [Fact]
    public async Task RedireccionHaciaOtroHostSeRechazaSinSeguirla()
    {
        using var destino = new HttpListener();
        var puertoDestino = PuertoLibre();
        destino.Prefixes.Add($"http://127.0.0.1:{puertoDestino}/");
        destino.Start();
        var peticionesAlDestino = 0;
        var escuchaDestino = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var ctx = await destino.GetContextAsync();
                    Interlocked.Increment(ref peticionesAlDestino);
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            }
            catch (HttpListenerException) { } catch (ObjectDisposedException) { }
        });

        using var origen = new HttpListener();
        var puertoOrigen = PuertoLibre();
        origen.Prefixes.Add($"http://127.0.0.1:{puertoOrigen}/");
        origen.Start();
        var escuchaOrigen = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var ctx = await origen.GetContextAsync();
                    ctx.Response.StatusCode = 302;
                    ctx.Response.Headers["Location"] = $"http://127.0.0.1:{puertoDestino}/repo.git/info/refs?service=git-upload-pack";
                    ctx.Response.Close();
                }
            }
            catch (HttpListenerException) { } catch (ObjectDisposedException) { }
        });

        var checkout = Path.Combine(Path.GetTempPath(), "vse-git-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var resultado = await ClonadorGit.ClonarAsync(
                new Uri($"http://127.0.0.1:{puertoOrigen}/repo.git"), checkout,
                new OpcionesClonado { MaxBytes = 10 * 1024 * 1024, Plazo = TimeSpan.FromSeconds(15) },
                permitirHttp: true);

            Assert.False(resultado.Exito);
            Assert.Equal(0, peticionesAlDestino);
        }
        finally
        {
            ClonadorGit.Borrar(checkout);
            destino.Stop();
            origen.Stop();
            await Task.WhenAny(Task.WhenAll(escuchaDestino, escuchaOrigen), Task.Delay(1000));
        }
    }

    private static int PuertoLibre()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var puerto = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return puerto;
    }
}
