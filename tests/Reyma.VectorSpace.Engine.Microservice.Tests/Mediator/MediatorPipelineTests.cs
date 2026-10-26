using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Reyma.Mediator.Behaviors.Base.Postgres;
using Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

namespace Reyma.VectorSpace.Engine.Microservice.Tests.Mediator;

[Collection("Api")]
public sealed class MediatorPipelineTests(ApiFactory factory)
{
    // El servicio retiro Postgres/EF Core en el item 1.9: no tiene ningun recurso transaccional de .NET
    // (Qdrant y el sidecar de embeddings no se enlistan en un TransactionScope). AddReymaMediatorDefault
    // (Reyma.Mediator.Base.Postgres.DependencyInjection) agregaba TransactionPipelineBehavior, que envuelve
    // cada solicitud en un TransactionScope con plazo TransactionManager.MaximumTimeout (10 min). La primera
    // ingesta real de innovapp-docs (629 chunks, una sola solicitud HTTP) tardo mas de 10 minutos: los puntos
    // quedaron guardados en Qdrant pero la solicitud respondio 500 "The transaction has aborted." al expirar
    // el scope. Se reemplazo AddReymaMediatorDefault por AddReymaMediator (Reyma.Mediator.Base.DependencyInjection),
    // que registra MediatR sin ese behavior. Esta prueba verifica el contenedor DI resultante.
    [Fact]
    public void PipelineDeMediatorNoIncluyeTransactionPipelineBehaviorYConservaElOrdenDeLosDemas()
    {
        List<ServiceDescriptor>? pipeline = null;

        using var factoryConPipelineCapturado = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                pipeline = services
                    .Where(d => !d.IsKeyedService && d.ServiceType == typeof(IPipelineBehavior<,>))
                    .ToList();
            }));
        // CreateClient fuerza la construccion del host de pruebas (y por lo tanto la ejecucion de
        // ConfigureServices/ConfigureTestServices) sin hacer ninguna solicitud HTTP.
        using var client = factoryConPipelineCapturado.CreateClient();

        Assert.NotNull(pipeline);
        var implementaciones = pipeline!.Select(d => d.ImplementationType).ToList();

        Assert.DoesNotContain(typeof(TransactionPipelineBehavior<,>), implementaciones);

        var indiceValidacion = implementaciones.IndexOf(typeof(ValidacionBehavior<,>));

        Assert.True(indiceValidacion >= 0, "ValidacionBehavior debe seguir registrado en el pipeline.");
    }
}
