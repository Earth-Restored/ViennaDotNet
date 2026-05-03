
using System.Text.Json.Serialization;

namespace Solace.ApiServer.Types.Common;

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
#pragma warning disable CA1707 // Identifiers should not contain underscores
        [JsonStringEnumMemberName("adv_zyki")]
        LEVEL_UP,
        [JsonStringEnumMemberName("redeemtappable")]
        TAPPABLE,
        [JsonStringEnumMemberName("item.unlocked")]
        JOURNAL_ITEM_UNLOCKED
#pragma warning restore CA1707 // Identifiers should not contain underscores
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
