using System.Text.Json;

namespace ViennaDotNet.Common;

public static class Json
{
    private static readonly JsonSerializerOptions options = new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly JsonSerializerOptions deseralizeOptions = new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions optionsIndented = new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, options);

    public static string SerializeIndented<T>(T value)
        => JsonSerializer.Serialize(value, optionsIndented);

    public static string Serialize<T>(T value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(value, options);

    public static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, deseralizeOptions);

    public static T? Deserialize<T>(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<T>(json, options);

    public static T? Deserialize<T>(Stream utf8Json)
        => JsonSerializer.Deserialize<T>(utf8Json, deseralizeOptions);

    public static ValueTask<T?> DeserializeAsync<T>(Stream utf8Stream, CancellationToken cancellationToken)
        => JsonSerializer.DeserializeAsync<T>(utf8Stream, deseralizeOptions, cancellationToken);

    public static object? Deserialize(string json, Type returnType)
        => JsonSerializer.Deserialize(json, returnType, deseralizeOptions);

    public static object? Deserialize(string json, Type returnType, JsonSerializerOptions options)
        => JsonSerializer.Deserialize(json, returnType, options);
}
