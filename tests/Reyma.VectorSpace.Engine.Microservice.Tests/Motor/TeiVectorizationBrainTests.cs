using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

/// <summary>
/// Cliente HTTP del sidecar (1.4) contra un sidecar simulado: prefijo solo en consultas, orden por <c>index</c>,
/// truncado Matryoshka + L2 en el cliente (1.8: Infinity no renormaliza) y rechazo de respuestas que desalinearian
/// vectores y chunks.
/// </summary>
public sealed class TeiVectorizationBrainTests
{
    private sealed class Sidecar(Func<JsonElement, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(string Path, JsonElement Body)> Peticiones { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            Peticiones.Add((request.RequestUri!.AbsolutePath, body));
            return responder(body);
        }
    }

    private sealed class Fabrica(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://sidecar.invalid/") };
    }

    private static TeiVectorizationBrain Brain(Sidecar sidecar, int dimension = 2, int porLlamada = 16, string? apiKey = null) =>
        new(new Fabrica(sidecar), Options.Create(new MotorOptions
        {
            Embeddings = new EmbeddingsOptions
            {
                UrlBase = "http://sidecar.invalid", Modelo = "modelo-x", Dimension = dimension, TextosPorLlamada = porLlamada,
                ApiKey = apiKey
            }
        }), NullLogger<TeiVectorizationBrain>.Instance);

    /// <summary>Responde un vector [i+1, 1, 5] por texto, en orden inverso de index, como podria hacerlo un sidecar real.</summary>
    private static HttpResponseMessage Respuesta(JsonElement body)
    {
        var n = body.GetProperty("input").GetArrayLength();
        var data = Enumerable.Range(0, n).Reverse().Select(i => new { index = i, embedding = new[] { i + 1f, 1f, 5f } });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { data }), Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task DocumentosVanSinPrefijoYConsultasConElPrefijoDelModelo()
    {
        var sidecar = new Sidecar(Respuesta);
        var brain = Brain(sidecar);

        await brain.EmbedDocumentsAsync(["un documento"], CancellationToken.None);
        await brain.EmbedQueryAsync("una pregunta", CancellationToken.None);

        Assert.All(sidecar.Peticiones, p => Assert.Equal("/v1/embeddings", p.Path));
        Assert.Equal("un documento", sidecar.Peticiones[0].Body.GetProperty("input")[0].GetString());
        Assert.Equal("query: una pregunta", sidecar.Peticiones[1].Body.GetProperty("input")[0].GetString());
        Assert.Equal("modelo-x", sidecar.Peticiones[0].Body.GetProperty("model").GetString());
        Assert.Equal(2, sidecar.Peticiones[0].Body.GetProperty("dimensions").GetInt32());
    }

    [Fact]
    public async Task OrdenaPorIndexTruncaALaDimensionYNormalizaL2()
    {
        var brain = Brain(new Sidecar(Respuesta));

        var vectores = await brain.EmbedDocumentsAsync(["a", "b", "c"], CancellationToken.None);

        Assert.Equal(3, vectores.Count);
        for (var i = 0; i < 3; i++)
        {
            // [i+1, 1, 5] truncado a 2 dimensiones y normalizado: la tercera componente no cuenta en la norma.
            var norma = Math.Sqrt((i + 1) * (i + 1) + 1);
            Assert.Equal(2, vectores[i].Length);
            Assert.Equal((i + 1) / norma, vectores[i][0], 6);
            Assert.Equal(1 / norma, vectores[i][1], 6);
        }
    }

    [Fact]
    public async Task ConDimensionIgualALaNativaNoTrunca()
    {
        // Respuesta() devuelve [i+1, 1, 5]: con Dimension=3 (la longitud nativa) no se recorta nada.
        var brain = Brain(new Sidecar(Respuesta), dimension: 3);

        var vectores = await brain.EmbedDocumentsAsync(["a"], CancellationToken.None);

        Assert.Equal(3, vectores[0].Length);
        var norma = Math.Sqrt(1 * 1 + 1 * 1 + 5 * 5);
        Assert.Equal(1 / norma, vectores[0][0], 6);
        Assert.Equal(1 / norma, vectores[0][1], 6);
        Assert.Equal(5 / norma, vectores[0][2], 6);
    }

    [Fact]
    public async Task PartePorTextosPorLlamadaSinPerderElOrden()
    {
        var sidecar = new Sidecar(Respuesta);
        var brain = Brain(sidecar, porLlamada: 2);

        var vectores = await brain.EmbedDocumentsAsync(["a", "b", "c", "d", "e"], CancellationToken.None);

        Assert.Equal([2, 2, 1], sidecar.Peticiones.Select(p => p.Body.GetProperty("input").GetArrayLength()));
        // El primer componente codifica la posicion dentro de cada llamada: 1,2 | 1,2 | 1.
        Assert.Equal([1f, 2f, 1f, 2f, 1f], vectores.Select(v => MathF.Round(v[0] / v[1])));
    }

    public static TheoryData<string> RespuestasDesalineadas() => new()
    {
        """{"data":[{"index":0,"embedding":[1,0]}]}""",
        """{"data":[{"index":0,"embedding":[1,0]},{"index":0,"embedding":[0,1]}]}""",
        """{"data":[{"index":0,"embedding":[1,0]},{"index":2,"embedding":[0,1]}]}""",
        """{"data":[{"index":0,"embedding":[1]},{"index":1,"embedding":[0]}]}""",
        """{"data":[{"index":0,"embedding":[0,0]},{"index":1,"embedding":[0,1]}]}""",
        """{}""",
    };

    [Theory]
    [MemberData(nameof(RespuestasDesalineadas))]
    public async Task RechazaRespuestasQueNoDanUnVectorValidoPorTexto(string json)
    {
        var brain = Brain(new Sidecar(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

        await Assert.ThrowsAsync<MotorNoDisponibleException>(() => brain.EmbedDocumentsAsync(["a", "b"], CancellationToken.None));
    }

    [Fact]
    public async Task UnErrorDelSidecarEsMotorNoDisponibleSinLaUrlNiElDetalle()
    {
        var brain = Brain(new Sidecar(_ => new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge)
        {
            Content = new StringContent("detalle interno de http://sidecar.invalid")
        }));

        var ex = await Assert.ThrowsAsync<MotorNoDisponibleException>(() => brain.EmbedQueryAsync("x", CancellationToken.None));
        Assert.Contains("413", ex.Message);
        Assert.DoesNotContain("sidecar.invalid", ex.Message);
    }

    [Fact]
    public async Task UnErrorDelSidecarConClaveConfiguradaNoFiltraLaClave()
    {
        const string clave = "clave-super-secreta-altheia";
        var brain = Brain(new Sidecar(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent($"detalle interno que menciona {clave} de forma accidental")
        }), apiKey: clave);

        var ex = await Assert.ThrowsAsync<MotorNoDisponibleException>(() => brain.EmbedQueryAsync("x", CancellationToken.None));
        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain(clave, ex.Message);
    }
}
