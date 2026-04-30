using System.Text.Json;
using System.Text.Json.Serialization;
using ViennaDotNet.BuildplateRenderer.Models.ResourcePacks;

namespace ViennaDotNet.BuildplateRenderer.JsonConverters;

public class VariantModelArrayConverter : JsonConverter<VariantModel[]>
{
    public override VariantModel[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<VariantModel>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    return [.. list];

                var item = JsonSerializer.Deserialize<VariantModel>(ref reader, options)!;
                list.Add(item);
            }

            throw new JsonException("Invalid JSON array");
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var single = JsonSerializer.Deserialize<VariantModel>(ref reader, options)!;
            return [single];
        }

        throw new JsonException($"Unexpected token {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, VariantModel[] value, JsonSerializerOptions options)
    {
        if (value.Length == 1)
        {
            JsonSerializer.Serialize(writer, value[0], options);
        }
        else
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}