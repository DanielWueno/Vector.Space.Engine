using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Reyma.VectorSpace.Engine.Microservice.Api;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Contrato de colecciones con identidad de prueba: 401 sin credencial valida, 200 para cualquier coleccion una vez
/// autenticado (item 1.14 retiro el control de acceso por coleccion: ya no hay 403 por tenencia) y 400 tras
/// autenticar si el cuerpo es invalido. Cada prueba usa su propio motor para que las llamadas no se mezclen.
/// </summary>
[Collection("Api")]
public sealed class ColeccionesTests(ApiFactory factory)
{
    private static readonly object CuerpoEmbeddings = new { textos = new[] { "hola" } };
    private static readonly object CuerpoBusqueda = new { consulta = "hola" };
    private static readonly object CuerpoIngesta = new
    {
        origen = new { tipo = "documentos", documentos = new[] { new { ruta = "a.md", contenido = "hola" } } }
    };

    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");

    private (WebApplicationFactory<Program> Api, MotorDePrueba Motor) ConMotor()
    {
        var motor = new MotorDePrueba();
        return (factory.ConIdentidad(servicios: MotorDePrueba.Registrar(motor)), motor);
    }

    public static TheoryData<string, string> Operaciones() => new()
    {
        { "embeddings", "Consultar" },
        { "busquedas", "Consultar" },
        { "ingestas", "Ingestar" },
    };

    private static object Cuerpo(string operacion) => operacion switch
    {
        "embeddings" => CuerpoEmbeddings,
        "busquedas" => CuerpoBusqueda,
        _ => CuerpoIngesta
    };

    // --- 401: sin credencial valida ---

    public static TheoryData<string> TokensInvalidos() => new()
    {
        "sin-token",
        "firma-ajena",
        "expirado",
        "audiencia-ajena",
        "emisor-ajeno",
    };

    private static string? TokenInvalido(string caso) => caso switch
    {
        "sin-token" => null,
        "firma-ajena" => Token(sub: SubAna, usuario: "ana", clave: ClaveAjena),
        "expirado" => Token(sub: SubAna, usuario: "ana", expira: DateTime.UtcNow.AddHours(-1)),
        "audiencia-ajena" => Token(sub: SubAna, usuario: "ana", audiencia: "otra.api"),
        "emisor-ajeno" => Token(sub: SubAna, usuario: "ana", emisor: "https://otro-emisor.invalid"),
        _ => throw new ArgumentOutOfRangeException(nameof(caso))
    };

    [Theory]
    [MemberData(nameof(TokensInvalidos))]
    public async Task CredencialInvalidaDevuelve401EnTodoElContratoSinLlegarAlMotor(string caso)
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenInvalido(caso));

        using (var sesion = await client.GetAsync("/api/sesion"))
            Assert.Equal(HttpStatusCode.Unauthorized, sesion.StatusCode);
        foreach (var operacion in new[] { "embeddings", "busquedas", "ingestas" })
        {
            using var post = await client.PostJson($"/api/colecciones/propia/{operacion}", Cuerpo(operacion));
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        }

        Assert.Empty(motor.Llamadas);
    }

    [Theory]
    [MemberData(nameof(Operaciones))]
    public async Task CredencialValidaAlcanzaSuColeccionYElMotorRecibeEsaColeccion(string operacion, string _)
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.PostJson($"/api/colecciones/propia/{operacion}", Cuerpo(operacion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var llamada = Assert.Single(motor.Llamadas);
        Assert.Equal("propia", llamada.Coleccion);
    }

    // --- Validacion del cuerpo, una vez autenticado ---

    public static TheoryData<string, string> CuerposInvalidos() => new()
    {
        { "embeddings", "null" },
        { "embeddings", "{}" },
        { "embeddings", """{"textos":[]}""" },
        { "embeddings", """{"textos":["  "]}""" },
        { "embeddings", "{\"textos\":[" + string.Join(",", Enumerable.Repeat("\"t\"", 65)) + "]}" },
        { "embeddings", "{\"textos\":[\"" + new string('a', 8193) + "\"]}" },
        { "busquedas", "{}" },
        { "busquedas", """{"consulta":"hola","topK":0}""" },
        { "busquedas", """{"consulta":"hola","topK":101}""" },
        { "ingestas", "{}" },
        { "ingestas", """{"origen":{"tipo":"otro"}}""" },
        { "ingestas", """{"origen":{"tipo":"documentos","documentos":[]}}""" },
        { "ingestas", """{"origen":{"tipo":"documentos","documentos":[{"ruta":"a.md"}]}}""" },
        { "ingestas", """{"origen":{"tipo":"documentos","documentos":[{"contenido":"x"}]}}""" },
        { "ingestas", """{"origen":{"tipo":"documentos","documentos":[{"ruta":"a.md","contenido":"x"},{"ruta":"a.md","contenido":"y"}]}}""" },
        { "ingestas", """{"origen":{"tipo":"documentos","ruta":"/srv","documentos":[{"ruta":"a.md","contenido":"x"}]}}""" },
        { "ingestas", """{"origen":{"tipo":"ruta"}}""" },
        { "ingestas", """{"origen":{"tipo":"ruta","ruta":"/srv","documentos":[]}}""" },
    };

    [Theory]
    [MemberData(nameof(CuerposInvalidos))]
    public async Task CuerpoInvalidoDevuelve400SinLlegarAlMotor(string operacion, string json)
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.PostAsync($"/api/colecciones/propia/{operacion}",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(motor.Llamadas);
    }

    [Fact]
    public async Task BusquedaSinTopKUsaElValorPorDefecto()
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/propia/busquedas", CuerpoBusqueda);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, Assert.Single(motor.Llamadas).Datos);
    }

    [Fact]
    public async Task OrigenRutaEstaReservadoYResponde501()
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/propia/ingestas",
            new { origen = new { tipo = "ruta", ruta = "/srv/repos/propia" } });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("1.7", await response.Content.ReadAsStringAsync());
        Assert.Empty(motor.Llamadas);
    }

    // --- Registro real del servicio con sus dependencias caidas ---

    [Theory]
    [MemberData(nameof(Operaciones))]
    public async Task ConElRegistroRealYDependenciasCaidasResponde503SinExponerLaConexion(string operacion, string _)
    {
        // ApiFactory apunta Qdrant y el sidecar a un puerto sin servicio. El motor real llega a la dependencia
        // y responde 503 (no 500 ni 501); el cuerpo no dice a que host ni puerto intento conectar.
        var api = factory.ConIdentidad();
        using var client = api.Cliente(TokenAna);

        using var propia = await client.PostJson($"/api/colecciones/propia/{operacion}", Cuerpo(operacion));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, propia.StatusCode);
        var cuerpo = await propia.Content.ReadAsStringAsync();
        Assert.DoesNotContain("127.0.0.1", cuerpo);
        Assert.DoesNotContain($":{ApiFactory.SinServicio}", cuerpo);
    }
}
