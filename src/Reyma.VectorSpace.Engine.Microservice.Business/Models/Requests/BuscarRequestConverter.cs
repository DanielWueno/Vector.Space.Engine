using Newtonsoft.Json;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Requests;

/// <summary>Conserva las cadenas de los filtros antes de que Newtonsoft intente interpretarlas como fechas.</summary>
public sealed class BuscarRequestConverter : JsonConverter<BuscarRequest>
{
    public override bool CanWrite => false;

    public override BuscarRequest? ReadJson(
        JsonReader reader, Type objectType, BuscarRequest? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException("Se esperaba un objeto de busqueda.");

        var previo = reader.DateParseHandling;
        try
        {
            reader.DateParseHandling = DateParseHandling.None;
            var request = new BuscarRequest();
            serializer.Populate(reader, request);
            return request;
        }
        finally
        {
            reader.DateParseHandling = previo;
        }
    }

    public override void WriteJson(JsonWriter writer, BuscarRequest? value, JsonSerializer serializer) =>
        throw new NotSupportedException("La escritura usa el serializador por defecto.");
}
