using System.Text.Json.Serialization;

namespace ViennaDotNet.ApiServer.Types.Workshop;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum State
{
    [JsonStringEnumMemberName("Empty")] EMPTY,
    [JsonStringEnumMemberName("Active")] ACTIVE,
    [JsonStringEnumMemberName("Completed")] COMPLETED,
    [JsonStringEnumMemberName("Locked")] LOCKED
}
