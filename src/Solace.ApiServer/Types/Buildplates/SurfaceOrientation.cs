using System.Text.Json.Serialization;

namespace ViennaDotNet.ApiServer.Types.Buildplates;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SurfaceOrientation
{
    [JsonStringEnumMemberName("Horizontal")] HORIZONTAL,
    [JsonStringEnumMemberName("Vertical")] VERTICAL    // TODO: unverified
}
