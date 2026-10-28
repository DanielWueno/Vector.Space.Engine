using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;
using Reyma.VectorSpace.Engine.Microservice.Business.Motor;
using static Reyma.VectorSpace.Engine.Microservice.Tests.IdentidadDePrueba;

namespace Reyma.VectorSpace.Engine.Microservice.Tests;

/// <summary>
/// Item 1.7, criterio (4): ingestas simultaneas y en cola acotadas; al rebasar la cola, 429 con Retry-After sin llegar
/// al motor. Embeddings y busquedas no pasan por el limite (superficies separadas, criterio 3).
/// </summary>
[Collection("Api")]
public sealed class LimiteDeIngestasTests(ApiFactory factory)
{
    private static readonly object CuerpoIngesta = new
    {
        origen = new { tipo = "documentos", documentos = new[] { new { ruta = "a.md", contenido = "hola" } } }
    };

    private static LimiteDeIngestas Limite(int simultaneas, int enCola) =>
        new(Options.Create(new LimiteDeIngestasOptions { MaximoSimultaneas = simultaneas, MaximoEnCola = enCola, SegundosParaReintentar = 7 }));

    [Fact]
    public async Task ConCupoYColaLlenosRechazaSinEsperarYAlLiberarEntraElSiguiente()
    {
        var limite = Limite(simultaneas: 1, enCola: 1);

        var primero = await limite.EntrarAsync(CancellationToken.None);
        var segundo = limite.EntrarAsync(CancellationToken.None);
        Assert.False(segundo.IsCompleted);

        var rechazo = await Assert.ThrowsAsync<IngestasSaturadasException>(() => limite.EntrarAsync(CancellationToken.None));
        Assert.Equal(7, rechazo.SegundosParaReintentar);

        primero.Dispose();
        primero.Dispose(); // liberar dos veces no abre un cupo de mas
        using var enCurso = await segundo.WaitAsync(TimeSpan.FromSeconds(5));
        var tercero = limite.EntrarAsync(CancellationToken.None);
        Assert.False(tercero.IsCompleted);
        await Assert.ThrowsAsync<IngestasSaturadasException>(() => limite.EntrarAsync(CancellationToken.None));
        enCurso.Dispose();
        (await tercero.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task CancelarMientrasEsperaLiberaSuLugarEnLaCola()
    {
        var limite = Limite(simultaneas: 1, enCola: 1);
        using var enCurso = await limite.EntrarAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var esperando = limite.EntrarAsync(cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => esperando);

        var otro = limite.EntrarAsync(CancellationToken.None);
        Assert.False(otro.IsCompleted);
        enCurso.Dispose();
        (await otro.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task SimultaneasDentroDelCupoCorrenALaVez()
    {
        var limite = Limite(simultaneas: 2, enCola: 0);
        using var a = await limite.EntrarAsync(CancellationToken.None);
        using var b = await limite.EntrarAsync(CancellationToken.None);
        await Assert.ThrowsAsync<IngestasSaturadasException>(() => limite.EntrarAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IngestaQueRebasaLaColaDevuelve429ConRetryAfterYNoBloqueaConsultas()
    {
        var motor = new MotorQueEspera();
        var configuracion = new Dictionary<string, string?>();
        configuracion["LimiteDeIngestas:MaximoSimultaneas"] = "1";
        configuracion["LimiteDeIngestas:MaximoEnCola"] = "0";
        configuracion["LimiteDeIngestas:SegundosParaReintentar"] = "45";
        var api = factory.ConIdentidad(configuracion, MotorQueEspera.Registrar(motor));
        using var ana = api.Cliente(Token(sub: SubAna, usuario: "ana"));
        using var beto = api.Cliente(Token(sub: Guid.NewGuid(), usuario: "beto"));

        var enCurso = ana.PostJson("/api/colecciones/propia/ingestas", CuerpoIngesta);
        await motor.Entro.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // El limite es global a la instancia: otra coleccion y otro usuario tambien esperan el mismo sidecar.
        using (var rechazada = await beto.PostJson("/api/colecciones/ajena/ingestas", CuerpoIngesta))
        {
            Assert.Equal((HttpStatusCode)429, rechazada.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(45), rechazada.Headers.RetryAfter?.Delta);
            Assert.Contains("ingestas", await rechazada.Content.ReadAsStringAsync());
        }

        // La validacion sigue yendo antes que el limite de admision: un cuerpo invalido responde 400, no 429.
        using (var invalida = await ana.PostJson("/api/colecciones/propia/ingestas", new { origen = new { tipo = "otro" } }))
            Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);

        // Embeddings y busquedas son otra superficie: responden mientras la ingesta ocupa el cupo.
        using (var embeddings = await ana.PostJson("/api/colecciones/propia/embeddings", new { textos = new[] { "hola" } }))
            Assert.Equal(HttpStatusCode.OK, embeddings.StatusCode);
        using (var busqueda = await ana.PostJson("/api/colecciones/propia/busquedas", new { consulta = "hola" }))
            Assert.Equal(HttpStatusCode.OK, busqueda.StatusCode);

        Assert.Equal(1, motor.Ingestas);
        motor.Soltar.SetResult();
        using (var primera = await enCurso)
            Assert.Equal(HttpStatusCode.OK, primera.StatusCode);

        // Terminada la primera, el cupo vuelve a estar libre.
        using (var siguiente = await beto.PostJson("/api/colecciones/ajena/ingestas", CuerpoIngesta))
            Assert.Equal(HttpStatusCode.OK, siguiente.StatusCode);
        Assert.Equal(2, motor.Ingestas);
    }

    [Fact]
    public async Task IngestaCanceladaPorElClienteLiberaElCupo()
    {
        var motor = new MotorQueEspera();
        var configuracion = new Dictionary<string, string?>();
        configuracion["LimiteDeIngestas:MaximoSimultaneas"] = "1";
        configuracion["LimiteDeIngestas:MaximoEnCola"] = "0";
        var api = factory.ConIdentidad(configuracion, MotorQueEspera.Registrar(motor));
        using var ana = api.Cliente(Token(sub: SubAna, usuario: "ana"));

        using var cts = new CancellationTokenSource();
        var cortada = ana.PostAsync("/api/colecciones/propia/ingestas",
            new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(CuerpoIngesta), System.Text.Encoding.UTF8, "application/json"), cts.Token);
        await motor.Entro.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cortada);
        await motor.Cancelada.Task.WaitAsync(TimeSpan.FromSeconds(10));

        motor.Soltar.SetResult();
        using var siguiente = await ana.PostJson("/api/colecciones/propia/ingestas", CuerpoIngesta);
        Assert.Equal(HttpStatusCode.OK, siguiente.StatusCode);
    }

    [Theory]
    [InlineData("LimiteDeIngestas:MaximoSimultaneas", "0")]
    [InlineData("LimiteDeIngestas:MaximoEnCola", "-1")]
    [InlineData("LimiteDeIngestas:SegundosParaReintentar", "0")]
    public void ConfiguracionIncoherenteImpideArrancar(string clave, string valor)
    {
        var configuracion = new Dictionary<string, string?>();
        configuracion[clave] = valor;
        var api = factory.ConIdentidad(configuracion);
        var error = Assert.ThrowsAny<Exception>(() => api.CreateClient());
        Assert.Contains("LimiteDeIngestas", error.ToString());
    }

    /// <summary>Motor cuya ingesta se queda esperando hasta <see cref="Soltar"/> (o hasta que se cancela la solicitud).</summary>
    private sealed class MotorQueEspera : IMotorDeEmbeddings, IMotorDeBusqueda, IMotorDeIngesta
    {
        private int _ingestas;

        public TaskCompletionSource Entro { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Soltar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Ingestas => Volatile.Read(ref _ingestas);

        public Task<EmbeddingsResponse> CalcularAsync(string coleccion, IReadOnlyList<string> textos, string? perfil, CancellationToken cancellationToken) =>
            Task.FromResult(new EmbeddingsResponse(coleccion, "modelo-prueba", 1, textos.Select(_ => new[] { 1f }).ToArray()));

        public Task<BusquedaResponse> BuscarAsync(
            string coleccion, string consulta, int topK, string? perfil, IReadOnlyList<string>? simbolosDefinidos, CancellationToken cancellationToken) =>
            Task.FromResult(new BusquedaResponse(coleccion, []));

        public async Task<IngestaResponse> IngerirAsync(string coleccion, IReadOnlyList<DocumentoRequest> documentos, string? perfil, bool resumen, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _ingestas);
            Entro.TrySetResult();
            try
            {
                await Soltar.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelada.TrySetResult();
                throw;
            }
            return new IngestaResponse(Guid.NewGuid(), coleccion, "completada", documentos.Count, documentos.Count, 0);
        }

        public static Action<IServiceCollection> Registrar(MotorQueEspera motor) => services =>
        {
            services.AddSingleton<IMotorDeEmbeddings>(motor);
            services.AddSingleton<IMotorDeBusqueda>(motor);
            services.AddSingleton<IMotorDeIngesta>(motor);
        };
    }
}
