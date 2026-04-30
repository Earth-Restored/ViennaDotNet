
using System.Text.Json.Serialization;

namespace ViennaDotNet.ApiServer.Types.Common;

public sealed record Token(
    Token.Type ClientType,
    Dictionary<string, string> ClientProperties,
    Rewards Rewards,
    Token.LifetimeE Lifetime
)
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Type
    {
        [JsonStringEnumMemberName("adv_zyki")]
        LEVEL_UP,
        [JsonStringEnumMemberName("redeemtappable")]
        TAPPABLE,
        [JsonStringEnumMemberName("item.unlocked")]
        JOURNAL_ITEM_UNLOCKED
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LifetimeE
    {
        [JsonStringEnumMemberName("Persistent")]
        PERSISTENT,
        [JsonStringEnumMemberName("Transient")]
        TRANSIENT
    }
}
