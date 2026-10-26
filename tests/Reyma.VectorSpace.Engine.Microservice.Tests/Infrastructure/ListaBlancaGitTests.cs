using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Git;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Infrastructure;

/// <summary>
/// Item 1.7.6: <see cref="ListaBlancaGit"/> es pura (sin I/O, sin red): valida la URL antes de que el servicio toque
/// la red. Mismo oraculo que <c>docs/evaluaciones/1.7.3/resultados/urls-windows.json</c> (1.7.3, prototipo), 3
/// admitidas y 20 rechazadas sobre la misma lista blanca de prueba; aqui contra la implementacion real portada a
/// Infrastructure (misma logica, sin cambios).
/// </summary>
public sealed class ListaBlancaGitTests
{
    private static readonly IReadOnlyList<OrigenGitPermitidoOptions> ListaDePrueba =
    [
        new() { Esquema = "https", Host = "dev.azure.com", Puerto = 443, PrefijoRuta = "/organizacion-ejemplo/" },
        new() { Esquema = "https", Host = "organizacion-ejemplo.visualstudio.com", Puerto = 443 }
    ];

    public static TheoryData<string> UrlsAdmitidas() =>
    [
        "https://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo",
        "https://dev.azure.com/organizacion-ejemplo/Mi%20Proyecto/_git/repo",
        "https://organizacion-ejemplo.visualstudio.com/Proyecto/_git/repo"
    ];

    [Theory]
    [MemberData(nameof(UrlsAdmitidas))]
    public void UrlEnLaListaBlancaSeAdmite(string url)
    {
        var (uri, error) = ListaBlancaGit.Validar(url, ListaDePrueba);

        Assert.Null(error);
        Assert.NotNull(uri);
    }

    // Mismas 20 URLs rechazadas que docs/evaluaciones/1.7.3/resultados/urls-windows.json, sin red: otra
    // organizacion, recorrido de directorio (crudo y codificado), sufijo de dominio, userinfo embebido (dos formas),
    // fragmento, query, otro esquema (http), otro puerto, github.com, IP de metadata de nube, puerto de Qdrant,
    // localhost, file://, ssh://, formato scp, ext::, inyeccion de argumento (--upload-pack), query con '?' y salto
    // de linea.
    public static TheoryData<string, string> UrlsRechazadas() => new()
    {
        { "https://dev.azure.com/otra-organizacion/Proyecto/_git/repo",
            "El origen 'https://dev.azure.com:443/otra-organizacion/Proyecto/_git/repo' no esta en la lista de origenes Git permitidos." },
        { "https://dev.azure.com/organizacion-ejemplo/../otra-organizacion/_git/repo",
            "El origen 'https://dev.azure.com:443/otra-organizacion/_git/repo' no esta en la lista de origenes Git permitidos." },
        { "https://dev.azure.com/organizacion-ejemplo/%2e%2e/otra-organizacion/_git/repo",
            "La URL no puede llevar puntos, barras ni nulos codificados." },
        { "https://dev.azure.com.atacante.example/organizacion-ejemplo/_git/repo",
            "El origen 'https://dev.azure.com.atacante.example:443/organizacion-ejemplo/_git/repo' no esta en la lista de origenes Git permitidos." },
        { "https://dev.azure.com@atacante.example/organizacion-ejemplo/_git/repo",
            "La URL no puede llevar usuario ni contrasena: el servicio usa su propia identidad." },
        { "https://atacante.example#@dev.azure.com/organizacion-ejemplo/",
            "La URL no puede llevar query ni fragmento." },
        { "https://usuario:secreto@dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo",
            "La URL no puede llevar usuario ni contrasena: el servicio usa su propia identidad." },
        { "http://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo",
            "El origen 'http://dev.azure.com:80/organizacion-ejemplo/Proyecto/_git/repo' no esta en la lista de origenes Git permitidos." },
        { "https://dev.azure.com:8443/organizacion-ejemplo/Proyecto/_git/repo",
            "El origen 'https://dev.azure.com:8443/organizacion-ejemplo/Proyecto/_git/repo' no esta en la lista de origenes Git permitidos." },
        { "https://github.com/DanielWueno/Rag.Context.Engine.git",
            "El origen 'https://github.com:443/DanielWueno/Rag.Context.Engine.git' no esta en la lista de origenes Git permitidos." },
        { "https://169.254.169.254/latest/meta-data/",
            "El origen 'https://169.254.169.254:443/latest/meta-data/' no esta en la lista de origenes Git permitidos." },
        { "http://127.0.0.1:6333/collections",
            "El origen 'http://127.0.0.1:6333/collections' no esta en la lista de origenes Git permitidos." },
        { "https://localhost/organizacion-ejemplo/",
            "El origen 'https://localhost:443/organizacion-ejemplo/' no esta en la lista de origenes Git permitidos." },
        { "file:///etc/passwd", "Esquema 'file' no admitido: solo https." },
        { "ssh://git@ssh.dev.azure.com/v3/organizacion-ejemplo/Proyecto/repo", "Esquema 'ssh' no admitido: solo https." },
        { "git@ssh.dev.azure.com:v3/organizacion-ejemplo/Proyecto/repo", "No es una URL absoluta." },
        { "ext::sh -c touch% /tmp/pwned", "La URL no puede llevar espacios ni caracteres de control." },
        { "--upload-pack=touch /tmp/pwned", "La URL no puede llevar espacios ni caracteres de control." },
        { "https://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo?version=GBmain",
            "La URL no puede llevar query ni fragmento." },
        { "https://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo\n-c core.sshCommand=x",
            "La URL no puede llevar espacios ni caracteres de control." },
    };

    [Theory]
    [MemberData(nameof(UrlsRechazadas))]
    public void UrlFueraDeLaListaBlancaSeRechazaSinTocarLaRed(string url, string motivoEsperado)
    {
        var (uri, error) = ListaBlancaGit.Validar(url, ListaDePrueba);

        Assert.Null(uri);
        Assert.Equal(motivoEsperado, error);
    }

    [Fact]
    public void SinNingunOrigenPermitidoTodaUrlSeRechaza()
    {
        var (uri, error) = ListaBlancaGit.Validar("https://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo", []);

        Assert.Null(uri);
        Assert.Contains("no esta en la lista de origenes Git permitidos", error);
    }

    [Fact]
    public void UrlVaciaONulaSeRechaza()
    {
        Assert.NotNull(ListaBlancaGit.Validar(null, ListaDePrueba).Error);
        Assert.NotNull(ListaBlancaGit.Validar("", ListaDePrueba).Error);
        Assert.NotNull(ListaBlancaGit.Validar("   ", ListaDePrueba).Error);
    }

    [Fact]
    public void UrlMasLargaQueElMaximoSeRechaza()
    {
        var url = "https://dev.azure.com/organizacion-ejemplo/" + new string('a', ListaBlancaGit.MaxCaracteresUrl);
        var (uri, error) = ListaBlancaGit.Validar(url, ListaDePrueba);

        Assert.Null(uri);
        Assert.Contains("admite como maximo", error);
    }
}
