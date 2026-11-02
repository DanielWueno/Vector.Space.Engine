using Newtonsoft.Json;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;

/// <summary>
/// Lista de cadenas sin conversiones implicitas (item 1.21.1): Newtonsoft convertiria en silencio <c>[1]</c> o
/// <c>[true]</c> en <c>["1"]</c>/<c>["True"]</c>, y un filtro de busqueda no debe aceptar un tipo incorrecto como si
/// fuera otro valor. Solo admite <c>null</c> o un arreglo cuyos elementos sean cadenas JSON; cualquier otra forma
/// (cadena suelta, objeto, numero, elemento nulo o no textual) es un error de deserializacion y la solicitud
/// responde 400 antes de llegar al motor.
/// </summary>
public sealed class ListaDeCadenasEstrictaConverter : JsonConverter<List<string>?>
{
    public override List<string>? ReadJson(
        JsonReader reader, Type objectType, List<string>? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException($"Se esperaba un arreglo de cadenas en '{reader.Path}'.");

        var valores = new List<string>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonToken.EndArray:
                    return valores;
                case JsonToken.String:
                    valores.Add((string)reader.Value!);
                    break;
                case JsonToken.Comment:
                    break;
                default:
                    throw new JsonSerializationException($"Se esperaba una cadena en '{reader.Path}'.");
            }
        }
        throw new JsonSerializationException("Arreglo de cadenas sin cerrar.");
    }

    public override void WriteJson(JsonWriter writer, List<string>? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteStartArray();
        foreach (var v in value)
            writer.WriteValue(v);
        writer.WriteEndArray();
    }
}
