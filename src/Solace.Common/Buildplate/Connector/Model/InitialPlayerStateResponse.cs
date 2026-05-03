using System.Text.Json.Serialization;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Solace.Buildplate.Connector.Model;
#pragma warning restore IDE0130 // Namespace does not match folder structure

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
#pragma warning disable CA1707 // Identifiers should not contain underscores
            ADVENTURE_XP,
            DEFENSE,
            EATING,
            HEALTH,
            MINING_SPEED,
            STRENGTH
#pragma warning restore CA1707 // Identifiers should not contain underscores
        }
    }
}