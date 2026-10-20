// 1.11: ingesta con el IIngestionPipeline REAL de Rag.Context.Engine y MaxTokensPerChunk como
// parametro. El CLI lo fija en 512 (IngestCommand.cs) y no lo enlaza a configuracion; aqui se
// construye la misma IngestionRequest que arma el CLI cambiando solo ese valor.
// Uso: Refragmentar <ruta> <coleccion> <max-tokens> <force:true|false> [repo-name=my-repo]
// Configuracion: la del CLI (appsettings.json de su bin + config compartida) y variables de entorno,
// igual que ingestar-control.ps1. Exige el Qdrant efimero: nunca rag-qdrant.
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RagEngine.Core.Abstractions;
using RagEngine.Core.Domain;
using RagEngine.Core.Extensions;
using RagEngine.Core.Infrastructure.Vectorization;
using RagEngine.Core.Utilities;

if (args.Length < 4)
{
    Console.Error.WriteLine("Uso: Refragmentar <ruta> <coleccion> <max-tokens> <force:true|false> [repo-name]");
    return 2;
}

var cliBin = Environment.GetEnvironmentVariable("RAG_CLI_BIN")
    ?? throw new InvalidOperationException("RAG_CLI_BIN: carpeta bin del CLI (su appsettings.json)");
if (Environment.GetEnvironmentVariable("Qdrant__HttpPort") != "16333")
    throw new InvalidOperationException("Solo contra el Qdrant efimero (Qdrant__HttpPort=16333).");

var host = Host.CreateDefaultBuilder()
    .UseContentRoot(cliBin)
    .ConfigureAppConfiguration(RagEnginePaths.InsertSharedConfigSource)
    .ConfigureLogging(l => l.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning))
    .ConfigureServices((ctx, services) =>
    {
        services.AddRagEngineCore(ctx.Configuration);
        services.AddRagEngineGeneration(ctx.Configuration);
    })
    .Build();

try
{
    var request = new IngestionRequest(
        RepositoryPath: args[0],
        CollectionName: args[1],
        Profile: ScanProfile.DotNetEnterprise,
        Options: new ChunkingOptions
        {
            MaxTokensPerChunk = int.Parse(args[2]),
            OverlapTokens = 64,
            RepositoryName = args.Length > 4 ? args[4] : "my-repo",
            BatchSize = 32
        },
        ForceReindex: bool.Parse(args[3]));
    var summary = await host.Services.GetRequiredService<IIngestionPipeline>().IngestRepositoryAsync(request);
    Console.WriteLine(JsonSerializer.Serialize(summary));
    return summary.ChunksIndexed == summary.ChunksGenerated ? 0 : 3;
}
finally
{
    host.Dispose();
    OnnxRuntimeLifetime.Shutdown();
}
