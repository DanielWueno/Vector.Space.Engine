using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qdrant.Client;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Vectorization;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure;

// Los mensajes no llevan host ni puerto: /health/ready es anonimo.

internal sealed class QdrantHealthCheck(QdrantClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.HealthAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Qdrant no responde.");
        }
    }
}

internal sealed class EmbeddingsHealthCheck(IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // TEI e Infinity exponen /health en la raiz.
            using var response = await httpClientFactory.CreateClient(TeiVectorizationBrain.HttpClientName)
                .GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"El sidecar de embeddings respondio {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("El sidecar de embeddings no responde.");
        }
    }
}
