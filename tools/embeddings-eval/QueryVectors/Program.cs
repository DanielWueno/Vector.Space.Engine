// 1.6: vectoriza las preguntas de un eval-set con los componentes de produccion de
// Rag.Context.Engine (SparseTokenizer + OnnxVectorizationBrain), sin Qdrant.
// Uso: dotnet run -- <appsettings.json del CLI> <salida.jsonl> <eval-set.json>...
// La configuracion la consume el programa; los overrides de ruta llegan por variables de entorno.
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RagEngine.Core.Abstractions;
using RagEngine.Core.Extensions;
using RagEngine.Core.Infrastructure.Vectorization;

if (args.Length < 3)
{
    Console.Error.WriteLine("Uso: QueryVectors <appsettings.json> <salida.jsonl> <eval-set.json>...");
    return 2;
}

var config = new ConfigurationBuilder()
    .AddJsonFile(Path.GetFullPath(args[0]))
    .AddEnvironmentVariables()
    .Build();
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddRagEngineCore(config);

try
{
    await using var provider = services.BuildServiceProvider();
    var brain = provider.GetRequiredService<IVectorizationBrain>();
    var sparse = provider.GetRequiredService<ISparseTokenizer>();

    await using var output = new StreamWriter(args[1]);
    foreach (var evalSet in args.Skip(2))
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(evalSet));
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var question = item.GetProperty("Question").GetString()!;
            var entries = sparse.Tokenize(question);
            var dense = await brain.GenerateEmbeddingAsync(question);
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                eval_set = Path.GetFileName(evalSet),
                question,
                sparse_indices = entries.Select(e => e.TermIndex),
                sparse_values = entries.Select(e => e.Weight),
                onnx_dense = dense,
            }));
        }
    }
    Console.Error.WriteLine($"brain: {brain.EmbeddingDimensions}d, ventana {brain.MaxSequenceLength}");
    return 0;
}
finally
{
    OnnxRuntimeLifetime.Shutdown();
}
