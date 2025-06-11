using System.Diagnostics;
using Uma.Uuid;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

using CICIBIEActivation = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Effect.Activation;
using CICIBIEType = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Effect.Type;

namespace ViennaDotNet.ApiServer.Utils;

public static class BoostUtils
{
    public static string? activatePotion(Boosts boosts, string itemId, long currentTime, Catalog.ItemsCatalog itemsCatalog)
    {
        Catalog.ItemsCatalog.Item item = itemsCatalog.getItem(itemId) ?? throw new ArgumentException(nameof(itemId));

        Catalog.ItemsCatalog.Item.BoostInfo? boostInfo = item.boostInfo;
        if (boostInfo is null || boostInfo.type is not Catalog.ItemsCatalog.Item.BoostInfo.Type.POTION)
        {
            throw new ArgumentException();
        }

        boosts.prune(currentTime);

        string instanceId = U.RandomUuid().ToString();
        long duration = boostInfo.duration is not null ? boostInfo.duration.Value : boostInfo.effects.Select(effect => effect.duration).DefaultIfEmpty().Max();

        int newIndex = -1;
        for (int index = 0; index < boosts.activeBoosts.Length; index++)
        {
            if (boosts.activeBoosts[index] is null)
            {
                newIndex = index;
                break;
            }
        }

        if (newIndex == -1)
        {
            return null;
        }

        boosts.activeBoosts[newIndex] = new Boosts.ActiveBoost(instanceId, itemId, currentTime, duration);

        return instanceId;
    }

    public static Catalog.ItemsCatalog.Item.BoostInfo.Effect[] getActiveEffects(Boosts boosts, long currentTime, Catalog.ItemsCatalog itemsCatalog)
    {
        LinkedList<Catalog.ItemsCatalog.Item.BoostInfo.Effect> effects = [];
        foreach (var activeBoost in boosts.activeBoosts)
        {
            if (activeBoost is null)
            {
                continue;
            }

            if (activeBoost.startTime + activeBoost.duration < currentTime)
            {
                continue;
            }

            Catalog.ItemsCatalog.Item? item = itemsCatalog.getItem(activeBoost.itemId);
            if (item is null || item.boostInfo is null)
            {
                continue;
            }

            foreach (var effect in item.boostInfo.effects
                .Where(effect => effect.activation switch
                {
                    CICIBIEActivation.INSTANT => false,
                    CICIBIEActivation.TRIGGERED => true,
                    CICIBIEActivation.TIMED => activeBoost.startTime + effect.duration >= currentTime,
                    _ => throw new UnreachableException(),
                }))
            {
                effects.AddLast(effect);
            }
        }

        return [.. effects];
    }

    public sealed record StatModiferValues(
        int maxPlayerHealthMultiplier,
        int attackMultiplier,
        int defenseMultiplier,
        int foodMultiplier,
        int miningSpeedMultiplier,
        int craftingSpeedMultiplier,
        int smeltingSpeedMultiplier,
        int tappableInteractionRadiusExtraMeters,
        bool keepHotbar,
        bool keepInventory,
        bool keepXp
    );

    public static StatModiferValues getActiveStatModifiers(Boosts boosts, long currentTime, Catalog.ItemsCatalog itemsCatalog)
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

        foreach (var effect in BoostUtils.getActiveEffects(boosts, currentTime, itemsCatalog))
        {
            switch (effect.type)
            {
                case CICIBIEType.HEALTH:
                    maxPlayerHealth += effect.value;
                    break;
                case CICIBIEType.STRENGTH:
                    attackMultiplier += effect.value;
                    break;
                case CICIBIEType.DEFENSE:
                    defenseMultiplier += effect.value;
                    break;
                case CICIBIEType.EATING:
                    foodMultiplier += effect.value;
                    break;
                case CICIBIEType.MINING_SPEED:
                    miningSpeedMultiplier += effect.value;
                    break;
                case CICIBIEType.CRAFTING:
                    craftingMultiplier += effect.value;
                    break;
                case CICIBIEType.SMELTING:
                    smeltingMultiplier += effect.value;
                    break;
                case CICIBIEType.TAPPABLE_RADIUS:
                    tappableInteractionRadius += effect.value;
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

    public static Effect boostEffectToApiResponse(Catalog.ItemsCatalog.Item.BoostInfo.Effect effect)
    {
        string effectTypeString = effect.type switch
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

        string activationString = effect.activation switch
        {
            CICIBIEActivation.INSTANT => "Instant",
            CICIBIEActivation.TIMED => "Timed",
            CICIBIEActivation.TRIGGERED => "Triggered",
            _ => throw new UnreachableException(),
        };

        return new Effect(
            effectTypeString,
            effect.activation == CICIBIEActivation.TIMED ? TimeFormatter.FormatDuration(effect.duration) : null,
            effect.type == CICIBIEType.RETENTION_BACKPACK || effect.type == CICIBIEType.RETENTION_HOTBAR || effect.type == CICIBIEType.RETENTION_XP ? null : effect.value,
            effect.type switch
            {
                CICIBIEType.HEALING or CICIBIEType.TAPPABLE_RADIUS => "Increment",
                CICIBIEType.ADVENTURE_XP or CICIBIEType.CRAFTING or CICIBIEType.DEFENSE or CICIBIEType.EATING or CICIBIEType.HEALTH or CICIBIEType.ITEM_XP or CICIBIEType.MINING_SPEED or CICIBIEType.SMELTING or CICIBIEType.STRENGTH => "Percentage",
                CICIBIEType.RETENTION_BACKPACK or CICIBIEType.RETENTION_HOTBAR or CICIBIEType.RETENTION_XP => null,
                _ => throw new UnreachableException(),
            },
            effect.type == CICIBIEType.CRAFTING || effect.type == CICIBIEType.SMELTING ? "UtilityBlock" : "Player",
            effect.applicableItemIds,

            effect.type switch
            {
                CICIBIEType.ITEM_XP => ["Tappable"],
                CICIBIEType.ADVENTURE_XP => ["Encounter"],
                _ => [],
            },
            activationString,
            effect.type == CICIBIEType.EATING ? "Health" : null
        );
    }
}
