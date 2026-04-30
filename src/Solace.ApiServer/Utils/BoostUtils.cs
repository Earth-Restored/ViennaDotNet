using System.Diagnostics;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

using CICIBIEActivation = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.ActivationE;
using CICIBIEType = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.TypeE;

namespace ViennaDotNet.ApiServer.Utils;

public static class BoostUtils
{
    public static Catalog.ItemsCatalogR.Item.BoostInfoR.Effect[] GetActiveEffects(Boosts boosts, long currentTime, Catalog.ItemsCatalogR itemsCatalog)
    {
        Dictionary<string, Catalog.ItemsCatalogR.Item.BoostInfoR> activeBoostsInfo = [];
        foreach (var activeBoost in boosts.ActiveBoosts)
        {
            if (activeBoost is null)
            {
                continue;
            }

            if (activeBoost.StartTime + activeBoost.Duration < currentTime)
            {
                continue;
            }

            Catalog.ItemsCatalogR.Item? item = itemsCatalog.GetItem(activeBoost.ItemId);
            if (item is null || item.BoostInfo is null)
            {
                continue;
            }

            Catalog.ItemsCatalogR.Item.BoostInfoR? existingBoostInfo = activeBoostsInfo.GetValueOrDefault(item.BoostInfo.Name);
            if (existingBoostInfo is not null && existingBoostInfo.Level > item.BoostInfo.Level)
            {
                continue;
            }

            activeBoostsInfo[item.BoostInfo.Name] = item.BoostInfo;
        }

        LinkedList<Catalog.ItemsCatalogR.Item.BoostInfoR.Effect> effects = [];
        foreach (Catalog.ItemsCatalogR.Item.BoostInfoR boostInfo in activeBoostsInfo.Values)
        {
            foreach (var effect in boostInfo.Effects
                .Where(effect => effect.Activation switch
                {
                    CICIBIEActivation.INSTANT => false,
                    CICIBIEActivation.TRIGGERED => true,
                    CICIBIEActivation.TIMED => true, // already filtered for expiry time above
                    _ => throw new UnreachableException(),
                }))
            {
                effects.AddLast(effect);
            }
        }

        return [.. effects];
    }

    public sealed record StatModiferValues(
        int MaxPlayerHealthMultiplier,
        int AttackMultiplier,
        int DefenseMultiplier,
        int FoodMultiplier,
        int MiningSpeedMultiplier,
        int CraftingSpeedMultiplier,
        int SmeltingSpeedMultiplier,
        int TappableInteractionRadiusExtraMeters,
        bool KeepHotbar,
        bool KeepInventory,
        bool KeepXp
    );

    public static StatModiferValues GetActiveStatModifiers(Boosts boosts, long currentTime, Catalog.ItemsCatalogR itemsCatalog)
    {
        int maxPlayerHealth = 0;
        int attackMultiplier = 0;
        int defenseMultiplier = 0;
        int foodMultiplier = 0;
        int miningSpeedMultiplier = 0;
        int craftingMultiplier = 0;
        int smeltingMultiplier = 0;
        int tappableInteractionRadius = 0;
        bool keepHotbar = false;
        bool keepInventory = false;
        bool keepXp = false;

        foreach (var effect in BoostUtils.GetActiveEffects(boosts, currentTime, itemsCatalog))
        {
            switch (effect.Type)
            {
                case CICIBIEType.HEALTH:
                    maxPlayerHealth += effect.Value;
                    break;
                case CICIBIEType.STRENGTH:
                    attackMultiplier += effect.Value;
                    break;
                case CICIBIEType.DEFENSE:
                    defenseMultiplier += effect.Value;
                    break;
                case CICIBIEType.EATING:
                    foodMultiplier += effect.Value;
                    break;
                case CICIBIEType.MINING_SPEED:
                    miningSpeedMultiplier += effect.Value;
                    break;
                case CICIBIEType.CRAFTING:
                    craftingMultiplier += effect.Value;
                    break;
                case CICIBIEType.SMELTING:
                    smeltingMultiplier += effect.Value;
                    break;
                case CICIBIEType.TAPPABLE_RADIUS:
                    tappableInteractionRadius += effect.Value;
                    break;
                case CICIBIEType.RETENTION_HOTBAR:
                    keepHotbar = true;
                    break;
                case CICIBIEType.RETENTION_BACKPACK:
                    keepInventory = true;
                    break;
                case CICIBIEType.RETENTION_XP:
                    keepXp = true;
                    break;
            }
        }

        return new StatModiferValues(
            maxPlayerHealth,
            attackMultiplier,
            defenseMultiplier,
            foodMultiplier,
            miningSpeedMultiplier,
            craftingMultiplier,
            smeltingMultiplier,
            tappableInteractionRadius,
            keepHotbar,
            keepInventory,
            keepXp
        );
    }

    public static int GetMaxPlayerHealth(Boosts boosts, long currentTime, Catalog.ItemsCatalogR itemsCatalog)
        => 20 + (20 * BoostUtils.GetActiveStatModifiers(boosts, currentTime, itemsCatalog).MaxPlayerHealthMultiplier) / 100;

    public static Effect BoostEffectToApiResponse(Catalog.ItemsCatalogR.Item.BoostInfoR.Effect effect, long boostDuration)
    {
        string effectTypeString = effect.Type switch
        {
            CICIBIEType.ADVENTURE_XP => "ItemExperiencePoints",
            CICIBIEType.CRAFTING => "CraftingSpeed",
            CICIBIEType.DEFENSE => "PlayerDefense",
            CICIBIEType.EATING => "FoodHealth",
            CICIBIEType.HEALING => "Health",
            CICIBIEType.HEALTH => "MaximumPlayerHealth",
            CICIBIEType.ITEM_XP => "ItemExperiencePoints",
            CICIBIEType.MINING_SPEED => "BlockDamage",
            CICIBIEType.RETENTION_BACKPACK => "RetainBackpack",
            CICIBIEType.RETENTION_HOTBAR => "RetainHotbar",
            CICIBIEType.RETENTION_XP => "RetainExperiencePoints",
            CICIBIEType.SMELTING => "SmeltingFuelIntensity",
            CICIBIEType.STRENGTH => "AttackDamage",
            CICIBIEType.TAPPABLE_RADIUS => "TappableInteractionRadius",
            _ => throw new UnreachableException(),
        };

        string activationString = effect.Activation switch
        {
            CICIBIEActivation.INSTANT => "Instant",
            CICIBIEActivation.TIMED => "Timed",
            CICIBIEActivation.TRIGGERED => "Triggered",
            _ => throw new UnreachableException(),
        };

        return new Effect(
            effectTypeString,
            effect.Activation == CICIBIEActivation.TIMED ? TimeFormatter.FormatDuration(boostDuration) : null,
            effect.Type == CICIBIEType.RETENTION_BACKPACK || effect.Type == CICIBIEType.RETENTION_HOTBAR || effect.Type == CICIBIEType.RETENTION_XP ? null : effect.Value,
            effect.Type switch
            {
                CICIBIEType.HEALING or CICIBIEType.TAPPABLE_RADIUS => "Increment",
                CICIBIEType.ADVENTURE_XP or CICIBIEType.CRAFTING or CICIBIEType.DEFENSE or CICIBIEType.EATING or CICIBIEType.HEALTH or CICIBIEType.ITEM_XP or CICIBIEType.MINING_SPEED or CICIBIEType.SMELTING or CICIBIEType.STRENGTH => "Percentage",
                CICIBIEType.RETENTION_BACKPACK or CICIBIEType.RETENTION_HOTBAR or CICIBIEType.RETENTION_XP => null,
                _ => throw new UnreachableException(),
            },
            effect.Type == CICIBIEType.CRAFTING || effect.Type == CICIBIEType.SMELTING ? "UtilityBlock" : "Player",
            effect.ApplicableItemIds,

            effect.Type switch
            {
                CICIBIEType.ITEM_XP => ["Tappable"],
                CICIBIEType.ADVENTURE_XP => ["Encounter"],
                _ => [],
            },
            activationString,
            effect.Type == CICIBIEType.EATING ? "Health" : null
        );
    }
}
