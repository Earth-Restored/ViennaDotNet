using Solace.ApiServer.Types.Common;

namespace Solace.ApiServer.Types.Boost;

internal sealed record Boosts(
    Boosts.Potion?[] Potions,
    Boosts.MiniFig[] MiniFigs,
    Boosts.ActiveEffect[] ActiveEffects,
    Dictionary<string, Boosts.ScenarioBoost[]> ScenarioBoosts,
    Boosts.StatusEffectsR StatusEffects,
    Dictionary<string, Boosts.MiniFigRecord> MiniFigRecords,
    string? Expiration
)
{
    internal sealed record Potion(
        bool Enabled,
        string ItemId,
        string InstanceId,
        string Expiration
    );

    internal sealed record MiniFig(
    // TODO
    );

    internal sealed record ActiveEffect(
        Effect Effect,
        string Expiration
    );

    internal sealed record ScenarioBoost(
        bool Enabled,
        string InstanceId,
        Effect[] Effects,
        string Expiration
    );

    internal sealed record StatusEffectsR(
        int? TappableInteractionRadius,
        int? ExperiencePointRate,
        int? ItemExperiencePointRates,
        int? AttackDamageRate,
        int? PlayerDefenseRate,
        int? BlockDamageRate,
        int? MaximumPlayerHealth,
        int? CraftingSpeed,
        int? SmeltingFuelIntensity,
        float? FoodHealthRate
    );

    internal sealed record MiniFigRecord(
    // TODO
    );
}