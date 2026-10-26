using System.IO.Compression;
using System.Net;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Item 1.7.5: validacion del origen 'proyecto' (.zip) sin necesitar Qdrant/TEI reales, porque un .zip
/// invalido se rechaza antes de tocar el motor (igual que el resto del contrato). Los
/// escenarios que si necesitan el motor real (202 -> completada con los mismos puntos, zips maliciosos, barrido de
/// ausentes) estan en Motor/IngestaProyectoZipIntegracionTests.cs, gateados por VSE_MOTOR_LOCAL.
/// </summary>
[Collection("Api")]
public sealed class IngestaProyectoZipTests(ApiFactory factory)
{
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");

    private static byte[] ZipValidoConUnMarkdown(string ruta = "docs/guia.md", string contenido = "# Guia\n\nTexto de prueba.\n")
    {
        using var memoria = new MemoryStream();
        using (var archivo = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entrada = archivo.CreateEntry(ruta);
            using var escritor = new StreamWriter(entrada.Open());
            escritor.Write(contenido);
        }
        return memoria.ToArray();
    }

    private static MultipartFormDataContent Multipart(byte[]? zip, string? carpetaRaiz = null, string? perfil = null)
    {
        var contenido = new MultipartFormDataContent();
        if (zip is not null)
            contenido.Add(new ByteArrayContent(zip), "zip", "proyecto.zip");
        if (carpetaRaiz is not null)
            contenido.Add(new StringContent(carpetaRaiz), "carpetaRaiz");
        if (perfil is not null)
            contenido.Add(new StringContent(perfil), "perfil");
        return contenido;
    }

    [Fact]
    public async Task SinCredencialValidaDevuelve401SinLlegarAlMotor()
    {
        using var client = factory.ConIdentidad().Cliente(token: null);
        using var response = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", Multipart(ZipValidoConUnMarkdown()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SinArchivoZipDevuelve400()
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", Multipart(zip: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnZipQueNoEsUnZipValidoDevuelve400SinCrearRegistroDeEstado()
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto",
            Multipart([1, 2, 3, 4, 5]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnZipSinDocumentosAdmitidosPorElPerfilDevuelve400()
    {
        // Ninguna extension del perfil DotNetEnterprise: el .zip es valido pero no trae nada que ingerir.
        var zip = ZipValidoConUnMarkdown(ruta: "imagen.png", contenido: "no es una extension admitida");
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", Multipart(zip));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CarpetaRaizQueNoEnvuelveTodasLasEntradasDevuelve400()
    {
        var zip = ZipValidoConUnMarkdown();
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto",
            Multipart(zip, carpetaRaiz: "repo-rama"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnZipValidoResponde202ConEstadoPendienteYLaConsultaDeEstadoFunciona()
    {
        var zip = ZipValidoConUnMarkdown();
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", Multipart(zip));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var cuerpo = await response.Leer<Newtonsoft.Json.Linq.JObject>();
        Assert.Equal("propia", (string)cuerpo["coleccion"]!);
        Assert.Equal(1, (int)cuerpo["documentosRecibidos"]!);
        Assert.Contains((string)cuerpo["estado"]!, new[] { "pendiente", "procesando" });
        var ingestaId = (string)cuerpo["ingestaId"]!;

        using var estado = await client.GetAsync($"/api/colecciones/propia/ingestas/{ingestaId}");
        Assert.Equal(HttpStatusCode.OK, estado.StatusCode);
        var cuerpoEstado = await estado.Leer<Newtonsoft.Json.Linq.JObject>();
        Assert.Equal(ingestaId, (string)cuerpoEstado["ingestaId"]!);
    }

    [Fact]
    public async Task ConsultarUnaIngestaQueNoExisteDevuelve404()
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.GetAsync($"/api/colecciones/propia/ingestas/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConsultarLaIngestaDeOtraColeccionDevuelve404NoLaDeLaPropia()
    {
        var zip = ZipValidoConUnMarkdown();
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var creada = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", Multipart(zip));
        var ingestaId = (string)(await creada.Leer<Newtonsoft.Json.Linq.JObject>())["ingestaId"]!;

        using var response = await client.GetAsync($"/api/colecciones/solo-lectura/ingestas/{ingestaId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
