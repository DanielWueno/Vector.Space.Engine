using Newtonsoft.Json;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;

/// <summary>Entero JSON Int32 o null, sin coercion de cadenas, decimales o booleanos.</summary>
public sealed class EnteroEstrictoConverter : JsonConverter<int?>
{
    public override int? ReadJson(
        JsonReader reader, Type objectType, int? existingValue, bool hasExistingValue, JsonSerializer serializer) =>
        reader.TokenType switch
        {
            JsonToken.Null => null,
            JsonToken.Integer when reader.Value is int value => value,
            JsonToken.Integer when reader.Value is long value && value is >= int.MinValue and <= int.MaxValue => (int)value,
            _ => throw new JsonSerializationException($"Se esperaba un entero JSON Int32 o null en '{reader.Path}'.")
        };

    public override void WriteJson(JsonWriter writer, int? value, JsonSerializer serializer) =>
        writer.WriteValue(value);
}
