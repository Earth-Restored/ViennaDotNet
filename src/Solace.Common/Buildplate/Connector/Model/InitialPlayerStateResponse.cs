using System.Text.Json.Serialization;

namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record InitialPlayerStateResponse(
    float Health,
    InitialPlayerStateResponse.BoostStatusEffect[] BoostStatusEffects
)
{
    public sealed record BoostStatusEffect(
        BoostStatusEffect.TypeE Type,
        int Value,
        long RemainingDuration
    )
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum TypeE
        {
            ADVENTURE_XP,
            DEFENSE,
            EATING,
            HEALTH,
            MINING_SPEED,
            STRENGTH
        }
    }
}