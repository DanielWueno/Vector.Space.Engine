using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Newtonsoft.Json.Linq;
using Reyma.VectorSpace.Engine.Microservice.Api;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using Swashbuckle.AspNetCore.Swagger;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// GET api/catalogo/* y el campo perfilLlm de las ingestas: lo que el catalogo anuncia es lo que la ingesta acepta, y
/// un perfil desconocido responde 400 antes de llegar al motor. Ademas, Swagger publica la descripcion de cada campo
/// (antes no se generaba el XML de documentacion y todos salian vacios).
/// </summary>
[Collection("Api")]
public sealed class CatalogoTests(ApiFactory factory)
{
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");

    public static TheoryData<string> Rutas() => new()
    {
        "/api/catalogo/perfiles-embeddings",
        "/api/catalogo/perfiles-llm",
        "/api/catalogo/perfiles-ingesta",
        "/api/catalogo/origenes-git",
    };

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task SinCredencialValidaDevuelve401(string ruta)
    {
        using var client = factory.ConIdentidad().Cliente(token: null);
        using var response = await client.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PerfilesDeIngestaDevuelveLosValoresQueAceptaLaIngesta()
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.GetAsync("/api/catalogo/perfiles-ingesta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var perfil = Assert.Single(await response.Leer<JArray>());
        Assert.Equal(PerfilDeIngesta.RagLocal112, (string)perfil["nombre"]!);
        Assert.True((bool)perfil["requiereRepositorioLogico"]!);
        Assert.False(string.IsNullOrWhiteSpace((string)perfil["descripcion"]!));
    }

    [Fact]
    public async Task OrigenesGitSaleDeLaListaBlancaConfigurada()
    {
        var config = new Dictionary<string, string?>
        {
            ["Motor:Git:OrigenesPermitidos:0:Host"] = "dev.azure.com",
            ["Motor:Git:OrigenesPermitidos:0:PrefijoRuta"] = "/Org-De-Prueba/",
            ["Motor:Git:OrigenesPermitidos:1:Host"] = "github.com",
            ["Motor:Git:OrigenesPermitidos:1:PrefijoRuta"] = "/Otra-Org/",
        };
        using var client = factory.ConIdentidad(config).Cliente(TokenAna);
        using var response = await client.GetAsync("/api/catalogo/origenes-git");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var origenes = await response.Leer<JArray>();
        Assert.Contains(origenes, o => (string)o["ejemplo"]! == "https://dev.azure.com/Org-De-Prueba/<ruta-del-repositorio>");
        Assert.Contains(origenes, o => (string)o["ejemplo"]! == "https://github.com/Otra-Org/<ruta-del-repositorio>");
    }

    [Fact]
    public async Task PerfilesDeEmbeddingsYLlmDevuelvenLoQueReportaElCatalogo()
    {
        var catalogo = new CatalogoDePrueba();
        using var client = factory.ConIdentidad(servicios: s => s.AddSingleton<IMotorDeCatalogo>(catalogo)).Cliente(TokenAna);

        using var embeddings = await client.GetAsync("/api/catalogo/perfiles-embeddings");
        using var llm = await client.GetAsync("/api/catalogo/perfiles-llm");

        Assert.Equal(HttpStatusCode.OK, embeddings.StatusCode);
        var perfil = Assert.Single(await embeddings.Leer<JArray>());
        Assert.Equal("tei-arctic-256", (string)perfil["nombre"]!);
        Assert.False((bool)perfil["disponible"]!);
        Assert.Equal("El servidor no responde.", (string)perfil["detalle"]!);

        Assert.Equal(HttpStatusCode.OK, llm.StatusCode);
        var perfilLlm = Assert.Single(await llm.Leer<JArray>());
        Assert.Equal("altheia-qwen-deep", (string)perfilLlm["nombre"]!);
        Assert.False((bool)perfilLlm["razonamiento"]!);
    }

    // --- perfilLlm en las ingestas: lo valida el catalogo real (sin red), antes del motor ---

    private static object IngestaCon(string? perfilLlm) => new
    {
        origen = new { tipo = "documentos", documentos = new[] { new { ruta = "a.md", contenido = "hola" } } },
        perfilLlm
    };

    [Theory]
    [InlineData("no-existe", "altheia-qwen-deep")]   // el 400 dice que perfiles si hay
    [InlineData("  ", "PerfilLlm no puede ser una cadena vacia")]
    public async Task UnPerfilLlmDesconocidoOBlancoEs400SinLlegarAlMotor(string perfilLlm, string mensaje)
    {
        var motor = new MotorDePrueba();
        using var client = factory.ConIdentidad(servicios: MotorDePrueba.Registrar(motor)).Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/propia/ingestas", IngestaCon(perfilLlm));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(mensaje, await response.Content.ReadAsStringAsync());
        Assert.Empty(motor.Llamadas);
    }

    [Fact]
    public async Task UnPerfilLlmRegistradoLlegaAlMotor()
    {
        var motor = new MotorDePrueba();
        var config = new Dictionary<string, string?> { ["Motor:Llm:Perfiles:altheia-qwen-fast:Modelo"] = "qwen3.8-27b-fast" };
        using var client = factory.ConIdentidad(config, MotorDePrueba.Registrar(motor)).Cliente(TokenAna);
        using var response = await client.PostJson("/api/colecciones/propia/ingestas", IngestaCon("altheia-qwen-fast"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(motor.Llamadas);
    }

    [Fact]
    public async Task UnPerfilLlmDesconocidoEnElZipEs400AntesDeAdmitirLaIngesta()
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "zip", "proyecto.zip" },
            { new StringContent("no-existe"), "perfilLlm" }
        };
        using var response = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no-existe", await response.Content.ReadAsStringAsync());
    }

    // --- Swagger: cada campo lleva su descripcion ---

    private static JObject Swagger(WebApplicationFactory<Program> api)
    {
        var documento = api.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        using var texto = new StringWriter();
        documento.SerializeAsV3(new OpenApiJsonWriter(texto));
        return JObject.Parse(texto.ToString());
    }

    private static string? Descripcion(JToken? propiedades, string nombre) =>
        (propiedades as JObject)?.Properties()
            .FirstOrDefault(p => string.Equals(p.Name, nombre, StringComparison.OrdinalIgnoreCase))?.Value["description"]?.ToString();

    private static JToken Operacion(JObject swagger, string sufijoRuta, string metodo) =>
        ((JObject)swagger["paths"]!).Properties().Single(p => p.Name.EndsWith(sufijoRuta, StringComparison.Ordinal)).Value[metodo]!;

    [Fact]
    public void SwaggerDescribeCadaCampoDelFormularioZip()
    {
        var swagger = Swagger(factory.ConIdentidad());
        var operacion = Operacion(swagger, "/colecciones/{coleccion}/ingestas/proyecto", "post");
        var propiedades = operacion["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!["properties"];

        foreach (var campo in new[] { "zip", "carpetaRaiz", "perfil", "resumen", "perfilLlm", "perfilIngesta", "repositorioLogico" })
            Assert.False(string.IsNullOrWhiteSpace(Descripcion(propiedades, campo)), $"El campo '{campo}' no tiene descripcion en Swagger.");
        Assert.Contains("envoltorio", Descripcion(propiedades, "carpetaRaiz"));
        Assert.Contains("api/catalogo/perfiles-llm", Descripcion(propiedades, "perfilLlm"));
        Assert.False(string.IsNullOrWhiteSpace((string?)operacion["parameters"]!.First(p => (string?)p["name"] == "coleccion")["description"]));
    }

    [Theory]
    [InlineData("IngestaRequest", "perfilLlm")]
    [InlineData("IngerirProyectoGitRequest", "url")]
    [InlineData("BuscarRequest", "espacio")]
    [InlineData("PerfilEmbeddingsInfoResponse", "disponible")]
    [InlineData("PerfilLlmInfoResponse", "razonamiento")]
    public void SwaggerDescribeLosCamposDeCuerposYRespuestas(string esquema, string campo)
    {
        var swagger = Swagger(factory.ConIdentidad());

        var propiedades = swagger["components"]!["schemas"]![esquema]!["properties"];

        Assert.False(string.IsNullOrWhiteSpace(Descripcion(propiedades, campo)), $"{esquema}.{campo} no tiene descripcion en Swagger.");
    }

    private sealed class CatalogoDePrueba : IMotorDeCatalogo
    {
        public Task<IReadOnlyList<PerfilEmbeddingsInfoResponse>> ListarPerfilesEmbeddingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PerfilEmbeddingsInfoResponse>>(
                [new("tei-arctic-256", "Snowflake/snowflake-arctic-embed-m-v2.0", 256, true, false, false, false, null, "El servidor no responde.")]);

        public Task<IReadOnlyList<PerfilLlmInfoResponse>> ListarPerfilesLlmAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PerfilLlmInfoResponse>>(
                [new("altheia-qwen-deep", "qwen3.8-27b-deep", 160, false, true, true, true, 117964, null)]);

        public IReadOnlyList<OrigenGitInfoResponse> ListarOrigenesGit() => [];

        public void ValidarPerfilLlm(string? perfilLlm)
        {
        }
    }
}
