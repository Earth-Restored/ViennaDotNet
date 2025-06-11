using System.Diagnostics;
using Uma.Uuid;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

using CICIBIEActivation = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Effect.Activation;
using CICIBIEType = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Effect.Type;

namespace ViennaDotNet.ApiServer.Utils;

public static class BoostUtils
{
    public static EarthDB.Query activatePotion(string playerId, string itemId, long currentTime, Catalog.ItemsCatalog itemsCatalog)
    {
        Catalog.ItemsCatalog.Item item = itemsCatalog.getItem(itemId) ?? throw new ArgumentException(nameof(itemId));

        Catalog.ItemsCatalog.Item.BoostInfo? boostInfo = item.boostInfo;
        if (boostInfo is null || boostInfo.type is not Catalog.ItemsCatalog.Item.BoostInfo.Type.POTION)
        {
            throw new ArgumentException();
        }

        string instanceId = U.RandomUuid().ToString();
        long duration = boostInfo.duration is not null ? boostInfo.duration.Value : boostInfo.effects.Select(effect => effect.duration).DefaultIfEmpty().Max();

        EarthDB.Query getQuery = new EarthDB.Query(true);
        getQuery.Get("boosts", playerId, typeof(Boosts));
        getQuery.Then(results =>
        {
            Boosts boosts = (Boosts)results.Get("boosts").Value;
            boosts.add(instanceId, itemId, currentTime, duration);
            boosts.prune(currentTime);
            EarthDB.Query updateQuery = new EarthDB.Query(true);
            updateQuery.Update("boosts", playerId, boosts);
            updateQuery.Extra("instanceId", instanceId);
            return updateQuery;
        });
        return getQuery;
    }

    public static Catalog.ItemsCatalog.Item.BoostInfo.Effect[] getActiveEffects(Boosts boosts, long currentTime, Catalog.ItemsCatalog itemsCatalog)
    {
        LinkedList<Catalog.ItemsCatalog.Item.BoostInfo.Effect> effects = [];
        foreach (Boosts.ActiveBoost activeBoost in boosts.getAll())
        {
            if (activeBoost.startTime + activeBoost.duration > currentTime)
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
                    CICIBIEActivation.TIMED => activeBoost.startTime + effect.duration <= currentTime,
                    _ => throw new UnreachableException(),
                }))
            {
                effects.AddLast(effect);
            }
        }

        return [.. effects];
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
                CICIBIEType.ITEM_XP=> ["Tappable"],
                CICIBIEType.ADVENTURE_XP=> ["Encounter"],
                _ => [],
            },
			activationString,
			effect.type == CICIBIEType.EATING ? "Health" : null
		);
    }
}
