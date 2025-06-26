using System.Text.Json;

namespace ViennaDotNet.Common;

public static class Json
{
    private static readonly JsonSerializerOptions options = new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, options);

    public static string Serialize<T>(T value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(value, options);

    public static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, options);

    public static T? Deserialize<T>(Stream utf8Json)
        => JsonSerializer.Deserialize<T>(utf8Json, options);

    public static ValueTask<T?> DeserializeAsync<T>(Stream utf8Stream, CancellationToken cancellationToken)
        => JsonSerializer.DeserializeAsync<T>(utf8Stream, options, cancellationToken);

    public static object? Deserialize(string json, Type returnType)
        => JsonSerializer.Deserialize(json, returnType, options);

    public static object? Deserialize(string json, Type returnType, JsonSerializerOptions options)
        => JsonSerializer.Deserialize(json, returnType, options);
}
