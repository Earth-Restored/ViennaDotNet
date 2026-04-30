using System.Text.Json.Serialization;

namespace ViennaDotNet.ApiServer.Models.Playfab;

internal sealed record PlayfabOkResponse(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("data")] object Data
);