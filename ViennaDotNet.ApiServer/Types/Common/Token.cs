using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace ViennaDotNet.ApiServer.Types.Common
{
    public record Token(
        Token.Type clientType,
        Dictionary<string, string> clientProperties,
        Rewards rewards,
        Token.Lifetime lifetime
    )
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public enum Type
        {
            [EnumMember(Value = "adv_zyki")]
            LEVEL_UP,
            [EnumMember(Value = "redeemtappable")]
            TAPPABLE,
            [EnumMember(Value = "item.unlocked")]
            JOURNAL_ITEM_UNLOCKED
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public enum Lifetime
        {
            [EnumMember(Value = "Persistent")]
            PERSISTENT,
            [EnumMember(Value = "Transient")]
            TRANSIENT
        }
    }
}
