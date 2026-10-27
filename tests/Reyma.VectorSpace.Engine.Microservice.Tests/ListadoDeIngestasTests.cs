using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Item 1.15: listado de ingestas de proyecto (hueco encontrado: el <c>ingestaId</c> de
/// <see cref="IngestaProyectoZipTests"/>/<see cref="IngestaProyectoGitTests"/> solo era visible en la respuesta 202,
/// sin forma de recuperarlo si se perdia) y el mensaje de <c>MarcarError</c>, que antes se descartaba. Item 1.16:
/// <c>coleccion</c> es un filtro opcional por query string -- sin el trae las ingestas de cualquier coleccion, util
/// cuando tampoco se sabe que coleccion buscar (ver <see cref="ColeccionesTests"/> para el catalogo de colecciones).
/// Usa un <see cref="MotorDeProyectoControlable"/> en vez del motor real (Qdrant/TEI no estan disponibles en esta
/// suite) para fijar de antemano si la ingesta termina en "completada" o en "error", sin depender de que falle la
/// conexion real ni de su mensaje exacto.
/// </summary>
[Collection("Api")]
public sealed class ListadoDeIngestasTests(ApiFactory factory)
{
    private static readonly string TokenAna = Token(sub: SubAna, usuario: "ana");

    private static MultipartFormDataContent Multipart()
    {
        var contenido = new MultipartFormDataContent();
        contenido.Add(new ByteArrayContent([1, 2, 3]), "zip", "proyecto.zip");
        return contenido;
    }

    [Fact]
    public async Task SinCredencialValidaDevuelve401()
    {
        using var client = factory.ConIdentidad().Cliente(token: null);
        using var response = await client.GetAsync("/api/colecciones/ingestas");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnaColeccionSinIngestasDevuelve200ConListaVacia()
    {
        using var client = factory.ConIdentidad().Cliente(TokenAna);
        using var response = await client.GetAsync("/api/colecciones/ingestas?coleccion=sin-ingestas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Leer<Newtonsoft.Json.Linq.JArray>();
        Assert.Empty(lista);
    }

    [Fact]
    public async Task ListarFiltradaPorColeccionDevuelveSoloLasDeEsaColeccionYNingunaDeOtra()
    {
        var motor = new MotorDeProyectoControlable(falla: false);
        using var client = factory.ConIdentidad(servicios: MotorDeProyectoControlable.Registrar(motor)).Cliente(TokenAna);

        using var primera = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", Multipart());
        using var segunda = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", Multipart());
        // Barrera de sincronizacion: con LimiteDeIngestas:MaximoSimultaneas en su valor por defecto (1), esta tercera
        // solicitud no se admite (su EntrarAsync no vuelve) hasta que el permiso de 'segunda' se libere, lo que solo
        // pasa despues de que su MarcarCompletada/MarcarError ya corrio (ver el finally de
        // IngerirProyectoZipCommand.ProcesarEnSegundoPlanoAsync). Para cuando esta solicitud responde, 'primera' y
        // 'segunda' ya tienen su estado final.
        using var deOtraColeccion = await client.PostAsync("/api/colecciones/ajena/ingestas/proyecto", Multipart());

        var idPrimera = (string)(await primera.Leer<Newtonsoft.Json.Linq.JObject>())["ingestaId"]!;
        var idSegunda = (string)(await segunda.Leer<Newtonsoft.Json.Linq.JObject>())["ingestaId"]!;

        using var response = await client.GetAsync("/api/colecciones/ingestas?coleccion=propia");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Leer<Newtonsoft.Json.Linq.JArray>();

        Assert.Equal(2, lista.Count);
        Assert.All(lista, item => Assert.Equal("propia", (string)item["coleccion"]!));
        Assert.All(lista, item => Assert.Equal("completada", (string)item["estado"]!));
        var ids = lista.Select(item => (string)item["ingestaId"]!).ToHashSet();
        Assert.Equal(new HashSet<string> { idPrimera, idSegunda }, ids);
    }

    [Fact]
    public async Task ListarSinFiltroDevuelveIngestasDeCualquierColeccion()
    {
        var motor = new MotorDeProyectoControlable(falla: false);
        using var client = factory.ConIdentidad(servicios: MotorDeProyectoControlable.Registrar(motor)).Cliente(TokenAna);

        using var propia = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", Multipart());
        // Misma barrera de sincronizacion que en la prueba de arriba.
        using var ajena = await client.PostAsync("/api/colecciones/ajena/ingestas/proyecto", Multipart());

        var idPropia = (string)(await propia.Leer<Newtonsoft.Json.Linq.JObject>())["ingestaId"]!;

        using var response = await client.GetAsync("/api/colecciones/ingestas");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Leer<Newtonsoft.Json.Linq.JArray>();

        // 'propia' ya tiene su estado final (la barrera lo garantiza); 'ajena' puede seguir en curso, pero su entrada
        // ya existe en el registro (Crear corre antes de responder el 202), que es lo que esta prueba verifica.
        Assert.Equal(2, lista.Count);
        var colecciones = lista.Select(item => (string)item["coleccion"]!).ToHashSet();
        Assert.Equal(new HashSet<string> { "propia", "ajena" }, colecciones);
        Assert.Contains(lista, item => (string)item["ingestaId"]! == idPropia);
    }

    [Fact]
    public async Task UnaIngestaEnEstadoErrorExponeElMensajeEnLaConsultaPuntualYEnElListado()
    {
        var motor = new MotorDeProyectoControlable(falla: true);
        using var client = factory.ConIdentidad(servicios: MotorDeProyectoControlable.Registrar(motor)).Cliente(TokenAna);

        using var creada = await client.PostAsync("/api/colecciones/propia/ingestas/proyecto", Multipart());
        var ingestaId = (string)(await creada.Leer<Newtonsoft.Json.Linq.JObject>())["ingestaId"]!;
        // Misma barrera de sincronizacion que arriba: fuerza a esperar a que el fallo de 'creada' ya este registrado.
        using var barrera = await client.PostAsync("/api/colecciones/otra/ingestas/proyecto", Multipart());

        using var estado = await client.GetAsync($"/api/colecciones/propia/ingestas/{ingestaId}");
        var cuerpoEstado = await estado.Leer<Newtonsoft.Json.Linq.JObject>();
        Assert.Equal("error", (string)cuerpoEstado["estado"]!);
        Assert.Equal(MotorDeProyectoControlable.MensajeDeFallo, (string)cuerpoEstado["mensajeError"]!);

        using var lista = await client.GetAsync("/api/colecciones/ingestas?coleccion=propia");
        var arreglo = await lista.Leer<Newtonsoft.Json.Linq.JArray>();
        var entrada = Assert.Single(arreglo);
        Assert.Equal(ingestaId, (string)entrada["ingestaId"]!);
        Assert.Equal("error", (string)entrada["estado"]!);
        Assert.Equal(MotorDeProyectoControlable.MensajeDeFallo, (string)entrada["mensajeError"]!);
    }

    /// <summary>
    /// Motor de proyecto controlable: evita Qdrant/TEI reales (no disponibles en esta suite) y deja fijo si la
    /// ingesta termina en "completada" o en "error", sin las condiciones de carrera del motor real.
    /// </summary>
    private sealed class MotorDeProyectoControlable(bool falla) : IMotorDeIngestaProyecto
    {
        public const string MensajeDeFallo = "fallo simulado de prueba (item 1.15)";

        public IReadOnlyList<DocumentoRequest> LeerProyectoZip(byte[] zip, string? carpetaRaiz) =>
            [new DocumentoRequest { Ruta = "docs/guia.md", Contenido = "contenido de prueba" }];

        public Task<ResultadoIngestaProyecto> ProcesarAsync(
            string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, bool resumen, CancellationToken cancellationToken) =>
            falla
                ? throw new InvalidOperationException(MensajeDeFallo)
                : Task.FromResult(new ResultadoIngestaProyecto(documentos.Count, 0, 0, 0));

        public static Action<IServiceCollection> Registrar(MotorDeProyectoControlable motor) => services =>
            services.AddSingleton<IMotorDeIngestaProyecto>(motor);
    }
}
