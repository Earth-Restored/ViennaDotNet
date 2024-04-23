using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace ViennaDotNet.ApiServer.Types.Workshop
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum State
    {
        [EnumMember(Value = "Empty")] EMPTY,
        [EnumMember(Value = "Active")] ACTIVE,
        [EnumMember(Value = "Completed")] COMPLETED,
        [EnumMember(Value = "Locked")] LOCKED
    }
}
