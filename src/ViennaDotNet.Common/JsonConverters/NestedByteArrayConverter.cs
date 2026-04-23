using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViennaDotNet.Common.JsonConverters;

public sealed class NestedByteArrayConverter : JsonConverter<byte[][]>
{
    public override byte[][] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of outer array.");
        }

        var outerList = new List<byte[]>();

        while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
        {
            if (reader.TokenType is not JsonTokenType.StartArray)
            {
                throw new JsonException("Expected start of inner numeric array.");
            }

            var innerList = new List<byte>();
            while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
            {
                innerList.Add(reader.GetByte());
            }

            outerList.Add([.. innerList]);
        }

        return [.. outerList];
    }

    public override void Write(Utf8JsonWriter writer, byte[][] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var array in value)
        {
            writer.WriteStartArray();
            foreach (var b in array)
            {
                writer.WriteNumberValue(b);
            }
            
            writer.WriteEndArray();
        }
        
        writer.WriteEndArray();
    }
}