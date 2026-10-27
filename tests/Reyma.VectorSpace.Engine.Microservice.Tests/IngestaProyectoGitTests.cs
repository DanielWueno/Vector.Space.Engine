using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Item 1.7.6: validacion del origen 'proyecto' por URL Git sin necesitar red ni Qdrant/TEI reales,
/// igual filosofia que <see cref="IngestaProyectoZipTests"/>: una URL invalida o fuera de la lista blanca se
/// rechaza antes de clonar nada. El escenario que si clona de verdad (contra un repo real de
/// Azure DevOps en la lista blanca, con los mismos puntos que el .zip del mismo commit) se verifica aparte, fuera de
/// la suite automatica (no depende de una identidad de Azure DevOps real ni de red en CI).
/// </summary>
[Collection("Api")]
public sealed class IngestaProyectoGitTests(ApiFactory factory)
{
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");

    private const string UrlDeEjemplo = "https://dev.azure.com/organizacion-ejemplo/Proyecto/_git/repo";

    private static object Cuerpo(string? url, string? carpetaRaiz = null, string? perfil = null) =>
        new { url, carpetaRaiz, perfil };

    [Fact]
    public async Task SinCredencialValidaDevuelve401SinLlegarAlMotor()
    {
        using var client = factory.ConIdentidad().Cliente(token: null);
        using var response = await client.PostJson("/api/colecciones/propia/ingestas/git", Cuerpo(UrlDeEjemplo));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SinUrlDevuelve400()
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/propia/ingestas/git", Cuerpo(url: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnaUrlFueraDeLaListaBlancaDevuelve400SinCrearRegistroDeEstadoNiTocarLaRed()
    {
        // Sin Motor:Git:OrigenesPermitidos configurado para este host de prueba (lista vacia por defecto): toda URL
        // se rechaza sin que el servicio intente resolver DNS ni abrir ninguna conexion.
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/propia/ingestas/git", Cuerpo(UrlDeEjemplo));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("https://dev.azure.com/otra-organizacion/Proyecto/_git/repo")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1:16333/collections")]
    [InlineData("https://localhost/organizacion-ejemplo/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ssh://git@ssh.dev.azure.com/v3/organizacion-ejemplo/Proyecto/repo")]
    public async Task UrlNoAdmisibleDevuelve400(string url)
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/propia/ingestas/git", Cuerpo(url));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnaUrlEnLaListaBlancaResponde202ConEstadoPendienteYLaConsultaDeEstadoFunciona()
    {
        // Lista blanca de este host apunta a un puerto local cerrado: el clonado en segundo plano fallara rapido
        // (conexion rechazada), pero eso no afecta la respuesta 202 inmediata ni la consulta de estado, que es lo
        // que prueba este caso (sin esperar a que el clonado termine).
        var config = new Dictionary<string, string?>();
        config["Motor:Git:OrigenesPermitidos:0:Esquema"] = "http";
        config["Motor:Git:OrigenesPermitidos:0:Host"] = "127.0.0.1";
        config["Motor:Git:OrigenesPermitidos:0:Puerto"] = "1";
        using var client = factory.ConIdentidad(config).Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/propia/ingestas/git", Cuerpo("http://127.0.0.1:1/repo"));

        var cuerpo = await response.Leer<Newtonsoft.Json.Linq.JObject>();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, $"{(int)response.StatusCode}: {cuerpo}");
        Assert.Equal("propia", (string)cuerpo["coleccion"]!);
        Assert.Equal(0, (int)cuerpo["documentosRecibidos"]!);
        Assert.Contains((string)cuerpo["estado"]!, new[] { "pendiente", "procesando" });
        var ingestaId = (string)cuerpo["ingestaId"]!;

        using var estado = await client.GetAsync($"/api/colecciones/propia/ingestas/{ingestaId}");
        Assert.Equal(HttpStatusCode.OK, estado.StatusCode);
        var cuerpoEstado = await estado.Leer<Newtonsoft.Json.Linq.JObject>();
        Assert.Equal(ingestaId, (string)cuerpoEstado["ingestaId"]!);
    }

    /// <summary>
    /// Item 1.17 (regresion): a diferencia del .zip (que lee el archivo de forma sincrona y conoce el total antes de
    /// admitir), el origen Git solo sabe cuantos documentos trajo DESPUES de clonar y escanear en segundo plano.
    /// Antes de este item, ese total nunca llegaba a <see cref="IRegistroDeIngestasProyecto"/>: <c>documentosRecibidos</c>
    /// se quedaba en el 0 del placeholder de admision para siempre, incluso con <c>estado: "completada"</c>, rompiendo
    /// la invariante "Recibidos = Indexados + SinCambios + Omitidos" documentada en <c>IngestaResponse</c>. Usa un
    /// <see cref="MotorGitControlable"/> (sin red ni whitelist real) para fijar un resultado conocido.
    /// </summary>
    [Fact]
    public async Task TrasCompletarDocumentosRecibidosReflejaElTotalRealNoElPlaceholderDeAdmision()
    {
        var motor = new MotorGitControlable(recibidos: 7, indexados: 5, sinCambios: 2);
        using var client = factory.ConIdentidad(servicios: MotorGitControlable.Registrar(motor)).Cliente(TokenAna);

        using var creada = await client.PostJson("/api/colecciones/propia/ingestas/git", Cuerpo(UrlDeEjemplo));
        var ingestaId = (string)(await creada.Leer<Newtonsoft.Json.Linq.JObject>())["ingestaId"]!;
        // Barrera de sincronizacion (mismo truco que ListadoDeIngestasTests): con LimiteDeIngestas:MaximoSimultaneas
        // en su valor por defecto (1), esta segunda solicitud no se admite hasta que el permiso de la primera se
        // libere, lo que solo pasa despues de que su MarcarCompletada ya corrio.
        using var barrera = await client.PostJson("/api/colecciones/otra/ingestas/git", Cuerpo(UrlDeEjemplo));

        using var estado = await client.GetAsync($"/api/colecciones/propia/ingestas/{ingestaId}");
        var cuerpo = await estado.Leer<Newtonsoft.Json.Linq.JObject>();
        Assert.Equal("completada", (string)cuerpo["estado"]!);
        Assert.Equal(7, (int)cuerpo["documentosRecibidos"]!);
        Assert.Equal(5, (int)cuerpo["documentosIndexados"]!);
        Assert.Equal(2, (int)cuerpo["documentosSinCambios"]!);
    }

    /// <summary>Motor Git controlable: sin red ni whitelist real, resultado fijo conocido para la prueba de arriba.</summary>
    private sealed class MotorGitControlable(int recibidos, int indexados, int sinCambios) : IMotorDeIngestaProyectoGit
    {
        public Uri ValidarUrlGit(string? url) => new(url!);

        public Task<ResultadoIngestaProyecto> ProcesarGitAsync(
            string coleccion, Uri url, string? carpetaRaiz, string? perfil, bool resumen, CancellationToken cancellationToken) =>
            Task.FromResult(new ResultadoIngestaProyecto(recibidos, indexados, sinCambios, Omitidos: 0, DocumentosEliminados: 0));

        public static Action<IServiceCollection> Registrar(MotorGitControlable motor) => services =>
            services.AddSingleton<IMotorDeIngestaProyectoGit>(motor);
    }
}
