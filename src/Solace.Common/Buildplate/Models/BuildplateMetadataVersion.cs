using System.Text.Json.Serialization;

namespace Solace.Buildplate.Model;

public sealed record BuildplateMetadataVersion(
    [property: JsonPropertyName("version")] int Version
);