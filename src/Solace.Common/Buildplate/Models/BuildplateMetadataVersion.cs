using System.Text.Json.Serialization;

namespace ViennaDotNet.Buildplate.Model;

public sealed record BuildplateMetadataVersion(
    [property: JsonPropertyName("version")] int Version
);