namespace Microsoft.Extensions.DependencyInjection;

using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Reyma.VectorSpace.Engine.Microservice.Business.Commands.Colecciones;
using Reyma.VectorSpace.Engine.Microservice.Business.Validacion;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Registros propios del servicio: validacion y puertos del motor.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class CustomContainer
{
    internal static void AddLocalServices(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidacionBehavior<,>));

        // Item 1.7: ingestas en curso y en espera acotadas por instancia (429 al rebasar la cola).
        services.AddOptions<LimiteDeIngestasOptions>()
            .Bind(config.GetSection(LimiteDeIngestasOptions.Seccion))
            .Validate(o => o.MaximoSimultaneas >= 1 && o.MaximoEnCola >= 0 && o.SegundosParaReintentar >= 1,
                $"{LimiteDeIngestasOptions.Seccion}: MaximoSimultaneas >= 1, MaximoEnCola >= 0 y SegundosParaReintentar >= 1.")
            .ValidateOnStart();
        services.AddSingleton<ILimiteDeIngestas, LimiteDeIngestas>();

        // Item 1.7.5: estado en memoria de las ingestas de proyecto (origen 'zip', asincrono).
        services.AddSingleton<IRegistroDeIngestasProyecto, RegistroDeIngestasProyecto>();

        // Item 1.4: motor portado (Qdrant + sidecar de embeddings) detras de los puertos IMotorDe*.
        services.AddMotorVectorial(config);
    }
}
