using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViennaDotNet.Common.JsonConverters;

public sealed class ByteArrayConverter : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array.");
        }

        var bytes = new List<byte>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            bytes.Add(reader.GetByte());
        }

        return [.. bytes];
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var b in value)
        {
            writer.WriteNumberValue(b);
        }

        writer.WriteEndArray();
    }
}