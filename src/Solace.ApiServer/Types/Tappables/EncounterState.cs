using System.Text.Json.Serialization;

namespace Solace.ApiServer.Types.Tappables;

public sealed record EncounterState(
    EncounterState.ActiveEncounterStateE ActiveEncounterState
)
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ActiveEncounterStateE
    {
        [JsonStringEnumMemberName("Pristine")] PRISTINE,
        [JsonStringEnumMemberName("Dirty")] DIRTY,
    }
}