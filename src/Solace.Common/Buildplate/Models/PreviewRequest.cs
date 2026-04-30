using System.Text.Json.Serialization;

namespace Solace.Buildplate.Model;

public sealed record PreviewRequest(
    [property: JsonPropertyName("serverDataBase64")] string ServerDataBase64,
    [property: JsonPropertyName("night")] bool Night
);