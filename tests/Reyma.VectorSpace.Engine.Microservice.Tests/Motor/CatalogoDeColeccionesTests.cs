using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using Reyma.VectorSpace.Engine.Microservice.Business;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Motor;

public sealed class CatalogoDeColeccionesTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ResumenSeLeeDelEsquemaAunqueLaMetadataDigaLoContrario(bool esquema, bool metadata)
    {
        var vectors = new Dictionary<string, object> { ["dense"] = new { size = 256, distance = "Cosine" } };
        if (esquema)
            vectors["dense-resumen"] = new { size = 256, distance = "Cosine" };
        using var handler = new Handler(HttpStatusCode.OK, new
        {
            result = new { config = new { @params = new { vectors }, metadata = new
            {
                perfil_embeddings = "tei-arctic-256", modelo = "fixture", dimension = 256,
                resumen_habilitado = metadata
            } } }
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://qdrant.invalid/") };
        using var grpc = new QdrantClient("127.0.0.1", 1);
        var store = new QdrantVectorStore(grpc, http, NullLogger<QdrantVectorStore>.Instance);

        var result = await store.LeerPerfilYEsquemaAsync("Case-Sensitive", default);

        Assert.Equal(esquema, result.TieneResumen);
        Assert.Equal(metadata, result.Perfil!.ResumenHabilitado);
        Assert.Equal("tei-arctic-256", result.Perfil.Perfil);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ColeccionSinMetadataConservaLaCapacidadRealDeResumen()
    {
        using var handler = new Handler(HttpStatusCode.OK, new
        {
            result = new { config = new { @params = new { vectors = new Dictionary<string, object>
            {
                ["dense-resumen"] = new { size = 256, distance = "Cosine" }
            } } } }
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://qdrant.invalid/") };
        using var grpc = new QdrantClient("127.0.0.1", 1);
        var store = new QdrantVectorStore(grpc, http, NullLogger<QdrantVectorStore>.Instance);
        var result = await store.LeerPerfilYEsquemaAsync("legacy", default);
        Assert.Null(result.Perfil);
        Assert.True(result.TieneResumen);
    }

    [Fact]
    public async Task RechazoRestNoSeConvierteEnMetadataAusente()
    {
        using var handler = new Handler(HttpStatusCode.ServiceUnavailable, new { });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://qdrant.invalid/") };
        using var grpc = new QdrantClient("127.0.0.1", 1);
        var store = new QdrantVectorStore(grpc, http, NullLogger<QdrantVectorStore>.Instance);
        await Assert.ThrowsAsync<MotorNoDisponibleException>(() => store.LeerPerfilYEsquemaAsync("test", default));
    }

    private sealed class Handler(HttpStatusCode status, object body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(body) });
        }
    }
}
