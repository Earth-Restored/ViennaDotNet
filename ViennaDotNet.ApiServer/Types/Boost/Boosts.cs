using ViennaDotNet.ApiServer.Types.Common;

namespace ViennaDotNet.ApiServer.Types.Boost;

public sealed record Boosts(
    Boosts.Potion[] potions,
    Boosts.MiniFig[] miniFigs,
    Boosts.ActiveEffect[] activeEffects,
    Dictionary<string, Boosts.ScenarioBoost[]> scenarioBoosts,
    Boosts.StatusEffects statusEffects,
    Dictionary<string, Boosts.MiniFigRecord> miniFigRecords,
    string? expiration
)
{
    public sealed record Potion(
        bool enabled,
        string itemId,
        string instanceId,
        string expiration
    );

    public sealed record MiniFig(
        // TODO
    );

    public sealed record ActiveEffect(
        Effect effect,
        string expiration
    );

    public sealed record ScenarioBoost(
        bool enabled,
        string instanceId,
        Effect[] effects,
        string expiration

    );

    public sealed record StatusEffects(
        int? tappableInteractionRadius,
        int? experiencePointRate,
        int? itemExperiencePointRates,
        int? attackDamageRate,
        int? playerDefenseRate,
        int? blockDamageRate,
        int? maximumPlayerHealth,
        int? craftingSpeed,
        int? smeltingFuelIntensity,
        int? foodHealthRate
    );

    public sealed record MiniFigRecord(
        // TODO
    );
}