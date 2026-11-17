using Newtonsoft.Json;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;

/// <summary>Cadena JSON o null, sin convertir numeros o booleanos en filtros textuales.</summary>
public sealed class CadenaEstrictaConverter : JsonConverter<string?>
{
    public override string? ReadJson(
        JsonReader reader, Type objectType, string? existingValue, bool hasExistingValue, JsonSerializer serializer) =>
        reader.TokenType switch
        {
            JsonToken.Null => null,
            JsonToken.String => (string)reader.Value!,
            _ => throw new JsonSerializationException($"Se esperaba una cadena o null en '{reader.Path}'.")
        };

    public override void WriteJson(JsonWriter writer, string? value, JsonSerializer serializer) =>
        writer.WriteValue(value);
}
