using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViennaDotNet.ApiServer;

public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string str = reader.GetString();

        if (DateTime.TryParseExact(str,
                                   Format,
                                   CultureInfo.InvariantCulture,
                                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                   out var dt))
        {
            return dt;
        }

        throw new JsonException($"Invalid date format. Expected format: {Format}");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Convert to UTC if not already
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        writer.WriteStringValue(utc.ToString(Format, CultureInfo.InvariantCulture));
    }
}