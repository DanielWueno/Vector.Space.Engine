// Aplica el FiltroPrevioDeResumen real (configuracion por defecto) a un volcado de puntos.
// Uso: dotnet run -- <entrada.json> <salida.json>
// Entrada: arreglo de { id, relative_path, chunk_type, content }. Salida: arreglo de { id, motivo } (motivo null = va al LLM).
using System.Text.Json;
using System.Text.Json.Nodes;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;

var opciones = new FiltroPrevioOptions();
var error = FiltroPrevioDeResumen.ErrorDeConfiguracion(opciones);
if (error is not null)
    throw new InvalidOperationException(error);
var filtro = new FiltroPrevioDeResumen(opciones);

var puntos = JsonNode.Parse(File.ReadAllText(args[0]))!.AsArray();
var salida = new JsonArray();
foreach (var p in puntos)
{
    var motivo = filtro.Motivo(
        (string)p!["relative_path"]!,
        (string?)p["chunk_type"],
        (string?)p["content"] ?? string.Empty);
    salida.Add(new JsonObject { ["id"] = (string)p["id"]!, ["motivo"] = motivo });
}

File.WriteAllText(args[1], salida.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
Console.WriteLine($"{filtro.Modelo} identidad {filtro.Identidad}: {puntos.Count} puntos");
foreach (var g in salida.GroupBy(x => (string?)x!["motivo"] ?? "(al LLM)").OrderBy(g => g.Key, StringComparer.Ordinal))
    Console.WriteLine($"  {g.Key}: {g.Count()}");
