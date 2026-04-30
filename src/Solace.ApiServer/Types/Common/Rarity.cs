using System.Text.Json.Serialization;

namespace ViennaDotNet.ApiServer.Types.Common;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Rarity
{
    [JsonStringEnumMemberName("Common")] COMMON,
    [JsonStringEnumMemberName("Uncommon")] UNCOMMON,
    [JsonStringEnumMemberName("Rare")] RARE,
    [JsonStringEnumMemberName("Epic")] EPIC,
    [JsonStringEnumMemberName("Legendary")] LEGENDARY,
    [JsonStringEnumMemberName("oobe")] OOBE
}
