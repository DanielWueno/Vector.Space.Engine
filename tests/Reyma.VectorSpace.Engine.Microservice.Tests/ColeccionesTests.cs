using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Contrato de colecciones con identidad de prueba: 401 sin credencial valida, 403 fuera del propio tenant (sin que
/// la solicitud llegue al motor) y 200 dentro de el. Cada prueba usa su propio motor para que las llamadas no se mezclen.
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
    private static readonly string TokenBeto = Token(sub: Guid.NewGuid(), usuario: "beto");

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

        using (var get = await client.GetAsync("/api/colecciones"))
            Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        using (var sesion = await client.GetAsync("/api/sesion"))
            Assert.Equal(HttpStatusCode.Unauthorized, sesion.StatusCode);
        foreach (var operacion in new[] { "embeddings", "busquedas", "ingestas" })
        {
            using var post = await client.PostJson($"/api/colecciones/propia/{operacion}", Cuerpo(operacion));
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        }

        Assert.Empty(motor.Llamadas);
    }

    // --- Tenencia: la propia coleccion si, la ajena no ---

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

    [Theory]
    [MemberData(nameof(Operaciones))]
    public async Task CredencialValidaNoAlcanzaLaColeccionDeOtroTenant(string operacion, string _)
    {
        var (api, motor) = ConMotor();

        using (var ana = api.Cliente(TokenAna))
        using (var response = await ana.PostJson($"/api/colecciones/ajena/{operacion}", Cuerpo(operacion)))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Simetrico: el dueno de 'ajena' tampoco alcanza 'propia', y si la suya.
        using (var beto = api.Cliente(TokenBeto))
        {
            using (var response = await beto.PostJson($"/api/colecciones/propia/{operacion}", Cuerpo(operacion)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await beto.PostJson($"/api/colecciones/ajena/{operacion}", Cuerpo(operacion)))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var llamada = Assert.Single(motor.Llamadas);
        Assert.Equal("ajena", llamada.Coleccion);
    }

    [Theory]
    [InlineData("no-listada")]
    [InlineData("PROPIA")]
    [InlineData("propia%20")]
    [InlineData("propia%0A")]
    [InlineData("propia%2F..%2Fajena")]
    [InlineData("propia..")]
    public async Task ColeccionNoListadaOVarianteDelNombreDevuelve403(string coleccion)
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.PostJson($"/api/colecciones/{coleccion}/embeddings", CuerpoEmbeddings);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(motor.Llamadas);
    }

    [Fact]
    public async Task LaOperacionTambienSeRestringe_ConsultarNoOtorgaIngestar()
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);

        using (var consulta = await client.PostJson("/api/colecciones/solo-lectura/embeddings", CuerpoEmbeddings))
            Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        using (var ingesta = await client.PostJson("/api/colecciones/solo-lectura/ingestas", CuerpoIngesta))
            Assert.Equal(HttpStatusCode.Forbidden, ingesta.StatusCode);

        Assert.Equal("embeddings", Assert.Single(motor.Llamadas).Operacion);
    }

    [Fact]
    public async Task SinAccesoResponde403AunqueElCuerpoSeaInvalido()
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/ajena/embeddings", new { textos = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(motor.Llamadas);
    }

    // --- Sujetos de la tabla: id, rol y cliente organizacional ---

    [Fact]
    public async Task ConcesionPorIdDeUsuarioUsaElClaimSub()
    {
        var (api, _) = ConMotor();

        using (var carla = api.Cliente(Token(sub: SubCarla)))
        using (var response = await carla.PostJson("/api/colecciones/por-id/embeddings", CuerpoEmbeddings))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var otro = api.Cliente(Token(sub: Guid.NewGuid(), usuario: SubCarla.ToString())))
        using (var response = await otro.PostJson("/api/colecciones/por-id/embeddings", CuerpoEmbeddings))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConcesionPorRolEsExacta()
    {
        var (api, _) = ConMotor();

        using (var analista = api.Cliente(Token(sub: Guid.NewGuid(), roles: ["lector", "analista"])))
        using (var response = await analista.PostJson("/api/colecciones/por-rol/embeddings", CuerpoEmbeddings))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var mayusculas = api.Cliente(Token(sub: Guid.NewGuid(), roles: ["Analista"])))
        using (var response = await mayusculas.PostJson("/api/colecciones/por-rol/embeddings", CuerpoEmbeddings))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConcesionPorClienteOrganizacional()
    {
        var (api, _) = ConMotor();

        using (var mismo = api.Cliente(Token(sub: Guid.NewGuid(), cliente: ClienteOrg)))
        using (var response = await mismo.PostJson("/api/colecciones/por-cliente/embeddings", CuerpoEmbeddings))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var otro = api.Cliente(Token(sub: Guid.NewGuid(), cliente: Guid.NewGuid())))
        using (var response = await otro.PostJson("/api/colecciones/por-cliente/embeddings", CuerpoEmbeddings))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TokenValidoSinClaimsDeIdentidadNoAlcanzaNada()
    {
        var (api, motor) = ConMotor();
        using var client = api.Cliente(Token());

        using (var lista = await client.GetAsync("/api/colecciones"))
        {
            Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
            Assert.Empty((await lista.Leer<ColeccionesAccesiblesResponse>()).Colecciones);
        }

        foreach (var coleccion in new[] { "propia", "ajena", "por-id", "por-rol", "por-cliente" })
        {
            using var response = await client.PostJson($"/api/colecciones/{coleccion}/embeddings", CuerpoEmbeddings);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Empty(motor.Llamadas);
    }

    // --- Listado ---

    [Fact]
    public async Task ListadoSoloMuestraLasColeccionesPropiasConSusOperaciones()
    {
        var (api, _) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.GetAsync("/api/colecciones");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var colecciones = (await response.Leer<ColeccionesAccesiblesResponse>()).Colecciones;
        Assert.Equal(["propia", "solo-lectura"], colecciones.Select(c => c.Nombre));
        Assert.Equal([OperacionColeccion.Consultar, OperacionColeccion.Ingestar], colecciones[0].Operaciones);
        Assert.Equal([OperacionColeccion.Consultar], colecciones[1].Operaciones);
    }

    // --- Validacion del cuerpo, una vez autorizado ---

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

    [Fact]
    public async Task OrigenRutaEnColeccionAjenaDevuelve403YNo501()
    {
        var (api, _) = ConMotor();
        using var client = api.Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/ajena/ingestas",
            new { origen = new { tipo = "ruta", ruta = "/srv/repos/ajena" } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Registro real del servicio con sus dependencias caidas ---

    [Theory]
    [MemberData(nameof(Operaciones))]
    public async Task ConElRegistroRealYDependenciasCaidasResponde503SinExponerLaConexion(string operacion, string _)
    {
        // ApiFactory apunta Qdrant y el sidecar a un puerto sin servicio. Autorizado, el motor real llega a la dependencia
        // y responde 503 (no 500 ni 501); el cuerpo no dice a que host ni puerto intento conectar.
        var api = factory.ConIdentidad();
        using var client = api.Cliente(TokenAna);

        using (var propia = await client.PostJson($"/api/colecciones/propia/{operacion}", Cuerpo(operacion)))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, propia.StatusCode);
            var cuerpo = await propia.Content.ReadAsStringAsync();
            Assert.DoesNotContain("127.0.0.1", cuerpo);
            Assert.DoesNotContain($":{ApiFactory.SinServicio}", cuerpo);
        }

        using (var ajena = await client.PostJson($"/api/colecciones/ajena/{operacion}", Cuerpo(operacion)))
            Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
    }
}
