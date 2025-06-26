using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using ViennaDotNet.ApiServer.Types.Catalog;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
using ViennaDotNet.StaticData;
using CICIBIEType = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.TypeE;
using CICIBIType = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.BoostInfoR.TypeE;
using CICICategory = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.CategoryE;
using CICIJEBehavior = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.JournalEntryR.BehaviorE;
using CICIJEBiome = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.JournalEntryR.BiomeE;
using CICIType = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.TypeE;
using CICIUseType = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.UseTypeE;
using CIJGCJGParentCollection = ViennaDotNet.StaticData.Catalog.ItemJournalGroupsCatalogR.JournalGroup.ParentCollectionE;
using CRCCRCategory = ViennaDotNet.StaticData.Catalog.RecipesCatalogR.CraftingRecipe.CategoryE;
using ItemsCatalog = ViennaDotNet.ApiServer.Types.Catalog.ItemsCatalog;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
public class CatalogController : ControllerBase
{
    private static Catalog catalog => Program.staticData.Catalog;

    [HttpGet("inventory/catalogv3")]
    public IActionResult GetItemsCatalog()
        => Content(Json.Serialize(new EarthApiResponse(MakeItemsCatalogApiResponse(catalog))), "application/json");

    [HttpGet("recipes")]
    public IActionResult GetRecipeCatalog()
        => Content(Json.Serialize(new EarthApiResponse(MakeRecipesCatalogApiResponse(catalog))), "application/json");

    [HttpGet("journal/catalog")]
    public IActionResult GetJournalCatalog()
        => Content(Json.Serialize(new EarthApiResponse(MakeJournalCatalogApiResponse(catalog))), "application/json");

    [HttpGet("products/catalog")]
    public IActionResult GetNFCBoostsCatalog()
        => Content(Json.Serialize(new EarthApiResponse(MakeNFCBoostsCatalogApiResponse(catalog))), "application/json");

    // TODO: cache these?
    private static ItemsCatalog MakeItemsCatalogApiResponse(Catalog catalog)
    {
        ItemsCatalog.ItemR[] items = [.. catalog.ItemsCatalog.Items.Select(item =>
        {
            string categoryString = item.Category switch
            {
                CICICategory.CONSTRUCTION => "Construction",
                CICICategory.EQUIPMENT => "Equipment",
                CICICategory.ITEMS => "Items",
                CICICategory.MOBS => "Mobs",
                CICICategory.NATURE => "Nature",
                CICICategory.BOOST_ADVENTURE_XP => "adventurexp",
                CICICategory.BOOST_CRAFTING => "crafting",
                CICICategory.BOOST_DEFENSE => "defense",
                CICICategory.BOOST_EATING => "eating",
                CICICategory.BOOST_HEALTH => "maxplayerhealth",
                CICICategory.BOOST_HOARDING => "hoarding",
                CICICategory.BOOST_ITEM_XP => "itemxp",
                CICICategory.BOOST_MINING_SPEED => "miningspeed",
                CICICategory.BOOST_RETENTION => "retention",
                CICICategory.BOOST_SMELTING => "smelting",
                CICICategory.BOOST_STRENGTH => "strength",
                CICICategory.BOOST_TAPPABLE_RADIUS => "tappableRadius",
                _ => throw new UnreachableException(),
            };

            string typeString = item.Type switch
            {
                CICIType.BLOCK => "Block",
                CICIType.ITEM => "Item",
                CICIType.TOOL => "Tool",
                CICIType.MOB => "Mob",
                CICIType.ENVIRONMENT_BLOCK => "EnvironmentBlock",
                CICIType.BOOST => "Boost",
                CICIType.ADVENTURE_SCROLL => "AdventureScroll",
                _ => throw new UnreachableException(),
            };

            string useTypeString = item.UseType switch
            {
                CICIUseType.NONE => "None",

                CICIUseType.BUILD => "Build",
                CICIUseType.BUILD_ATTACK => "BuildAttack",
                CICIUseType.INTERACT => "Interact",
                CICIUseType.INTERACT_AND_BUILD => "InteractAndBuild",
                CICIUseType.DESTROY => "Destroy",
                CICIUseType.USE => "Use",
                CICIUseType.CONSUME => "Consume",
                _ => throw new UnreachableException(),
            };

            string alternativeUseTypeString = item.AlternativeUseType switch
            {
                CICIUseType.NONE => "None",

                CICIUseType.BUILD => "Build",
                CICIUseType.BUILD_ATTACK => "BuildAttack",
                CICIUseType.INTERACT => "Interact",
                CICIUseType.INTERACT_AND_BUILD => "InteractAndBuild",
                CICIUseType.DESTROY => "Destroy",
                CICIUseType.USE => "Use",
                CICIUseType.CONSUME => "Consume",
                _ => throw new UnreachableException(),
            };

            int health;
            if (item.BlockInfo is not null)
            {
                health = item.BlockInfo.BreakingHealth;
            }
            else if (item.ToolInfo is not null)
            {
                health = item.ToolInfo.MaxWear;
            }
            else if (item.MobInfo is not null)
            {
                health = item.MobInfo.Health;
            }
            else
            {
                health = 0;
            }

            int blockDamage;
            if (item.ToolInfo is not null)
            {
                blockDamage = item.ToolInfo.BlockDamage;
            }
            else
            {
                blockDamage = 0;
            }

            int mobDamage;
            if (item.ToolInfo is not null)
            {
                mobDamage = item.ToolInfo.MobDamage;
            }
            else if (item.ProjectileInfo is not null)
            {
                mobDamage = item.ProjectileInfo.MobDamage;
            }
            else
            {
                mobDamage = 0;
            }

            ItemsCatalog.ItemR.ItemData.BlockMetadataR? blockMetadata;
            if (item.BlockInfo is not null)
            {
                blockMetadata = new ItemsCatalog.ItemR.ItemData.BlockMetadataR(item.BlockInfo.BreakingHealth, item.BlockInfo.EfficiencyCategory);
            }
            else if (item.MobInfo is not null)
            {
                blockMetadata = new ItemsCatalog.ItemR.ItemData.BlockMetadataR(item.MobInfo.Health, "instant");
            }
            else
            {
                blockMetadata = null;
            }

            BoostMetadata? boostMetadata;
            if (item.BoostInfo is not null)
            {
                string boostTypeString = item.BoostInfo.Type switch
                {
                    CICIBIType.POTION => "Potion",
                    CICIBIType.INVENTORY_ITEM => "InventoryItem",
                    _ => throw new UnreachableException(),
                };

                string boostAttributeString = item.BoostInfo.Effects[0].Type switch
                {
                    CICIBIEType.ADVENTURE_XP => "ItemExperiencePoints",
                    CICIBIEType.CRAFTING => "Crafting",
                    CICIBIEType.DEFENSE => "Defense",
                    CICIBIEType.EATING => "Eating",
                    CICIBIEType.HEALING => "Healing",
                    CICIBIEType.HEALTH => "MaximumPlayerHealth",
                    CICIBIEType.ITEM_XP => "ItemExperiencePoints",
                    CICIBIEType.MINING_SPEED => "MiningSpeed",
                    CICIBIEType.RETENTION_BACKPACK or CICIBIEType.RETENTION_HOTBAR or CICIBIEType.RETENTION_XP => "Retention",
                    CICIBIEType.SMELTING => "Smelting",
                    CICIBIEType.STRENGTH => "Strength",
                    CICIBIEType.TAPPABLE_RADIUS => "TappableInteractionRadius",
                    _ => throw new UnreachableException(),
                };

                boostMetadata = new BoostMetadata(
                    item.BoostInfo.Name,
                    boostTypeString,
                    boostAttributeString,
                    false,
                    item.BoostInfo.CanBeRemoved,
                    TimeFormatter.FormatDuration(item.BoostInfo.Duration),
                    true,
                    item.BoostInfo.Level,
                    [.. item.BoostInfo.Effects.Select(effect => BoostUtils.BoostEffectToApiResponse(effect, item.BoostInfo.Duration))],
                    item.BoostInfo.TriggeredOnDeath ? "Death" : null,
                    null
                );
            }
            else
            {
                boostMetadata = null;
            }

            ItemsCatalog.ItemR.ItemData.JournalMetadataR? journalMetadata;
            if (item.JournalEntry is not null)
            {
                string behaviorString = item.JournalEntry.Behavior switch
                {
                    CICIJEBehavior.NONE => "None",
                    CICIJEBehavior.PASSIVE => "Passive",
                    CICIJEBehavior.HOSTILE => "Hostile",
                    CICIJEBehavior.NEUTRAL => "Neutral",
                    _ => throw new UnreachableException(),
                };

                string biomeString = item.JournalEntry.Biome switch
                {
                    CICIJEBiome.NONE => "None",
                    CICIJEBiome.OVERWORLD => "Overworld",
                    CICIJEBiome.NETHER => "Hell",
                    CICIJEBiome.BIRCH_FOREST => "BirchForest",
                    CICIJEBiome.DESERT => "Desert",
                    CICIJEBiome.FLOWER_FOREST => "FlowerForest",
                    CICIJEBiome.FOREST => "Forest",
                    CICIJEBiome.ICE_PLAINS => "IcePlains",
                    CICIJEBiome.JUNGLE => "Jungle",
                    CICIJEBiome.MESA => "Mesa",
                    CICIJEBiome.MUSHROOM_ISLAND => "MushroomIsland",
                    CICIJEBiome.OCEAN => "Ocean",
                    CICIJEBiome.PLAINS => "Plains",
                    CICIJEBiome.RIVER => "River",
                    CICIJEBiome.ROOFED_FOREST => "RoofedForest",
                    CICIJEBiome.SAVANNA => "Savanna",
                    CICIJEBiome.SUNFLOWER_PLAINS => "SunFlowerPlains",
                    CICIJEBiome.SWAMP => "Swampland",
                    CICIJEBiome.TAIGA => "Taiga",
                    CICIJEBiome.WARM_OCEAN => "WarmOcean",
                    _ => throw new UnreachableException(),
                };

                journalMetadata = new ItemsCatalog.ItemR.ItemData.JournalMetadataR(
                    item.JournalEntry.Group,
                    item.Experience.Journal,
                    item.JournalEntry.Order,
                    behaviorString,
                    biomeString
                );
            }
            else
            {
                journalMetadata = null;
            }

            return new ItemsCatalog.ItemR(
                item.Id,
                new ItemsCatalog.ItemR.ItemData(
                    item.Name,
                    item.Aux,
                    typeString,
                    useTypeString,
                    0,
                    item.ConsumeInfo?.Heal,
                    0,
                    mobDamage,
                    blockDamage,
                    health,
                    blockMetadata,
                    new ItemsCatalog.ItemR.ItemData.ItemMetadataR(
                        useTypeString,
                        alternativeUseTypeString,
                        mobDamage,
                        blockDamage,
                        null,
                        0,
                        item.ConsumeInfo is not null ? item.ConsumeInfo.Heal : 0,
                        item.ToolInfo?.EfficiencyCategory,
                        health
                    ),
                    boostMetadata,
                    journalMetadata,
                    item.JournalEntry is not null && item.JournalEntry.Sound is not null ? new ItemsCatalog.ItemR.ItemData.AudioMetadataR(
                        new Dictionary<string, string>() { ["journal"] = item.JournalEntry.Sound },
                        item.JournalEntry.Sound
                    ) : null,
                    new Dictionary<string, object>()
                ),
                categoryString,
                Enum.Parse<Types.Common.Rarity>(item.Rarity.ToString()),
                1,
                item.Stackable,
                item.FuelInfo is not null ? new Types.Common.BurnRate(item.FuelInfo.BurnTime, item.FuelInfo.HeatPerSecond) : null,
                item.FuelInfo is not null && item.FuelInfo.ReturnItemId is not null ? [new ItemsCatalog.ItemR.ReturnItem(item.FuelInfo.ReturnItemId, 1)] : [],
                item.ConsumeInfo is not null && item.ConsumeInfo.ReturnItemId is not null ? [new ItemsCatalog.ItemR.ReturnItem(item.ConsumeInfo.ReturnItemId, 1)] : [],
                item.Experience.Tappable,
                new Dictionary<string, int?>() { ["tappable"] = item.Experience.Tappable, ["encounter"] = item.Experience.Encounter, ["crafting"] = item.Experience.Crafting },
                false
            );
        })];

        Dictionary<string, ItemsCatalog.EfficiencyCategory> efficiencyCategories = [];
        foreach (Catalog.ItemEfficiencyCategoriesCatalogR.EfficiencyCategory efficiencyCategory in catalog.ItemEfficiencyCategoriesCatalog.EfficiencyCategories)
        {
            efficiencyCategories[efficiencyCategory.Name] = new ItemsCatalog.EfficiencyCategory(
                new ItemsCatalog.EfficiencyCategory.EfficiencyMapR(
                    efficiencyCategory.Hand,
                    efficiencyCategory.Hoe,
                    efficiencyCategory.Axe,
                    efficiencyCategory.Shovel,
                    efficiencyCategory.Pickaxe_1,
                    efficiencyCategory.Pickaxe_2,
                    efficiencyCategory.Pickaxe_3,
                    efficiencyCategory.Pickaxe_4,
                    efficiencyCategory.Pickaxe_5,
                    efficiencyCategory.Sword,
                    efficiencyCategory.Sheers
                )
            );
        }

        return new ItemsCatalog(items, efficiencyCategories);
    }

    private static RecipesCatalog MakeRecipesCatalogApiResponse(Catalog catalog)
    {
        RecipesCatalog.CraftingRecipe[] crafting = [.. catalog.RecipesCatalog.Crafting.Select(recipe =>
        {
            string categoryString = recipe.Category switch
            {
                CRCCRCategory.CONSTRUCTION => "Construction",
                CRCCRCategory.EQUIPMENT => "Equipment",
                CRCCRCategory.ITEMS => "Items",
                CRCCRCategory.NATURE => "Nature",
                _ => throw new UnreachableException(),
            };

            return new RecipesCatalog.CraftingRecipe(
                    recipe.Id,
                    categoryString,
                    TimeFormatter.FormatDuration(recipe.Duration * 1000),
                    [.. recipe.Ingredients.Select(ingredient => new RecipesCatalog.CraftingRecipe.Ingredient(ingredient.PossibleItemIds, ingredient.Count))],
                    new RecipesCatalog.CraftingRecipe.OutputR(recipe.Output.ItemId, recipe.Output.Count),
                    [.. recipe.ReturnItems.Select(returnItem => new RecipesCatalog.CraftingRecipe.ReturnItem(returnItem.ItemId, returnItem.Count))],
                    false
            );
        })];

        RecipesCatalog.SmeltingRecipe[] smelting = [.. catalog.RecipesCatalog.Smelting.Select(recipe =>
        {
            return new RecipesCatalog.SmeltingRecipe(
                recipe.Id,
                recipe.HeatRequired,
                recipe.Input,
                new RecipesCatalog.SmeltingRecipe.OutputR(recipe.Output, 1),
                recipe.ReturnItemId is not null ? [new RecipesCatalog.SmeltingRecipe.ReturnItem(recipe.ReturnItemId, 1)] : [],
                false
            );
        })];

        return new RecipesCatalog(crafting, smelting);
    }

    private static JournalCatalog MakeJournalCatalogApiResponse(Catalog catalog)
    {
        Dictionary<string, JournalCatalog.Item> items = [];
        foreach (Catalog.ItemJournalGroupsCatalogR.JournalGroup group in catalog.ItemJournalGroupsCatalog.Groups)
        {
            string parentCollectionString = group.ParentCollection switch
            {
                CIJGCJGParentCollection.BLOCKS => "Blocks",
                CIJGCJGParentCollection.ITEMS_CRAFTED => "ItemsCrafted",
                CIJGCJGParentCollection.ITEMS_SMELTED => "ItemsSmelted",
                CIJGCJGParentCollection.MOBS => "Mobs",
                _ => throw new UnreachableException(),
            };

            items[group.Name] = new JournalCatalog.Item(
                    group.Id,
                    parentCollectionString,
                    group.Order,
                    group.Order,
                    group.DefaultSound,
                    false,
                    "200526.173531"
            );
        }

        return new JournalCatalog(items);
    }

    private static NFCBoost[] MakeNFCBoostsCatalogApiResponse(Catalog catalog)
    {
        // TODO
        return [];
    }
}
