using System.Text.Json.Serialization;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Solace.Buildplate.Model;
#pragma warning restore IDE0130 // Namespace does not match folder structure

public sealed record BuildplateMetadataVersion(
    [property: JsonPropertyName("version")] int Version
);