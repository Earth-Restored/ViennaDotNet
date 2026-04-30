using ViennaDotNet.ApiServer.Types.Common;

namespace ViennaDotNet.ApiServer.Types.Boost;

public sealed record Boosts(
    Boosts.Potion?[] Potions,
    Boosts.MiniFig[] MiniFigs,
    Boosts.ActiveEffect[] ActiveEffects,
    Dictionary<string, Boosts.ScenarioBoost[]> ScenarioBoosts,
    Boosts.StatusEffectsR StatusEffects,
    Dictionary<string, Boosts.MiniFigRecord> MiniFigRecords,
    string? Expiration
)
{
    public sealed record Potion(
        bool Enabled,
        string ItemId,
        string InstanceId,
        string Expiration
    );

    public sealed record MiniFig(
    // TODO
    );

    public sealed record ActiveEffect(
        Effect Effect,
        string Expiration
    );

    public sealed record ScenarioBoost(
        bool Enabled,
        string InstanceId,
        Effect[] Effects,
        string Expiration
    );

    public sealed record StatusEffectsR(
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

    public sealed record MiniFigRecord(
    // TODO
    );
}