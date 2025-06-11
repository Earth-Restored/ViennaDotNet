using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Diagnostics;
using ViennaDotNet.ApiServer.Types.Catalog;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.StaticData;
using CICIBIEActivation = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Effect.Activation;
using CICIBIEType = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Effect.Type;
using CICIBIType = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Type;
using CICICategory = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.Category;
using CICIJEBehavior = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.JournalEntry.Behavior;
using CICIJEBiome = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.JournalEntry.Biome;
using CICIType = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.Type;
using CICIUseType = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.UseType;
using CIJGCJGParentCollection = ViennaDotNet.StaticData.Catalog.ItemJournalGroupsCatalog.JournalGroup.ParentCollection;
using CRCCRCategory = ViennaDotNet.StaticData.Catalog.RecipesCatalog.CraftingRecipe.Category;
using ItemsCatalog = ViennaDotNet.ApiServer.Types.Catalog.ItemsCatalog;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
public class CatalogController : ControllerBase
{
    private static Catalog catalog => Program.staticData.catalog;

    [HttpGet("inventory/catalogv3")]
    public IActionResult GetItemsCatalog()
        => Content(JsonConvert.SerializeObject(new EarthApiResponse(makeItemsCatalogApiResponse(catalog))), "application/json");

    [HttpGet("recipes")]
    public IActionResult GetRecipeCatalog()
        => Content(JsonConvert.SerializeObject(new EarthApiResponse(makeRecipesCatalogApiResponse(catalog))), "application/json");

    [HttpGet("journal/catalog")]
    public IActionResult GetJournalCatalog()
        => Content(JsonConvert.SerializeObject(new EarthApiResponse(makeJournalCatalogApiResponse(catalog))), "application/json");

    [HttpGet("products/catalog")]
    public IActionResult GetNFCBoostsCatalog()
        => Content(JsonConvert.SerializeObject(new EarthApiResponse(makeNFCBoostsCatalogApiResponse(catalog))), "application/json");

    // TODO: cache these?
    private static ItemsCatalog makeItemsCatalogApiResponse(Catalog catalog)
    {
        ItemsCatalog.Item[] items = [.. catalog.itemsCatalog.items.Select(item =>
        {
            string categoryString = item.category switch
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

            string typeString = item.type switch
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

            string useTypeString = item.useType switch
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

            string alternativeUseTypeString = item.alternativeUseType switch
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
            if (item.blockInfo is not null)
            {
                health = item.blockInfo.breakingHealth;
            }
            else if (item.toolInfo is not null)
            {
                health = item.toolInfo.maxWear;
            }
            else if (item.mobInfo is not null)
            {
                health = item.mobInfo.health;
            }
            else
            {
                health = 0;
            }

            int blockDamage;
            if (item.toolInfo is not null)
            {
                blockDamage = item.toolInfo.blockDamage;
            }
            else
            {
                blockDamage = 0;
            }

            int mobDamage;
            if (item.toolInfo is not null)
            {
                mobDamage = item.toolInfo.mobDamage;
            }
            else if (item.projectileInfo is not null)
            {
                mobDamage = item.projectileInfo.mobDamage;
            }
            else
            {
                mobDamage = 0;
            }

            ItemsCatalog.Item.ItemData.BlockMetadata? blockMetadata;
            if (item.blockInfo is not null)
            {
                blockMetadata = new ItemsCatalog.Item.ItemData.BlockMetadata(item.blockInfo.breakingHealth, item.blockInfo.efficiencyCategory);
            }
            else if (item.mobInfo is not null)
            {
                blockMetadata = new ItemsCatalog.Item.ItemData.BlockMetadata(item.mobInfo.health, "instant");
            }
            else
            {
                blockMetadata = null;
            }

            BoostMetadata? boostMetadata;
            if (item.boostInfo is not null)
            {
                string boostTypeString = item.boostInfo.type switch
                {
                    CICIBIType.POTION => "Potion",
                    CICIBIType.INVENTORY_ITEM => "InventoryItem",
                    _ => throw new UnreachableException(),
                };

                string boostAttributeString = item.boostInfo.effects[0].type switch
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
                    item.boostInfo.name,
                    boostTypeString,
                    boostAttributeString,
                    false,
                    item.boostInfo.canBeRemoved,
                    item.boostInfo.duration is not null ? TimeFormatter.FormatDuration(item.boostInfo.duration.Value) : null,
                    true,
                    item.boostInfo.level,
                    [.. item.boostInfo.effects.Select(BoostUtils.boostEffectToApiResponse)],
                    item.boostInfo.triggeredOnDeath ? "Death" : null,
                    null
                );
            }
            else
            {
                boostMetadata = null;
            }

            ItemsCatalog.Item.ItemData.JournalMetadata? journalMetadata;
            if (item.journalEntry is not null)
            {
                string behaviorString = item.journalEntry.behavior switch
                {
                    CICIJEBehavior.NONE => "None",
                    CICIJEBehavior.PASSIVE => "Passive",
                    CICIJEBehavior.HOSTILE => "Hostile",
                    CICIJEBehavior.NEUTRAL => "Neutral",
                    _ => throw new UnreachableException(),
                };

                string biomeString = item.journalEntry.biome switch
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

                journalMetadata = new ItemsCatalog.Item.ItemData.JournalMetadata(
                    item.journalEntry.group,
                    item.experience.journal,
                    item.journalEntry.order,
                    behaviorString,
                    biomeString
                );
            }
            else
            {
                journalMetadata = null;
            }

            return new ItemsCatalog.Item(
                item.id,
                new ItemsCatalog.Item.ItemData(
                    item.name,
                    item.aux,
                    typeString,
                    useTypeString,
                    0,
                    item.consumeInfo?.heal,
                    0,
                    mobDamage,
                    blockDamage,
                    health,
                    blockMetadata,
                    new ItemsCatalog.Item.ItemData.ItemMetadata(
                        useTypeString,
                        alternativeUseTypeString,
                        mobDamage,
                        blockDamage,
                        null,
                        0,
                        item.consumeInfo is not null ? item.consumeInfo.heal : 0,
                        item.toolInfo?.efficiencyCategory,
                        health
                    ),
                    boostMetadata,
                    journalMetadata,
                    item.journalEntry is not null && item.journalEntry.sound is not null ? new ItemsCatalog.Item.ItemData.AudioMetadata(
                        new Dictionary<string, string>() { ["journal"] = item.journalEntry.sound },
                        item.journalEntry.sound
                    ) : null,
                    new Dictionary<string, object>()
                ),
                categoryString,
                Enum.Parse<Types.Common.Rarity>(item.rarity.ToString()),
                1,
                item.stackable,
                item.fuelInfo is not null ? new Types.Common.BurnRate(item.fuelInfo.burnTime, item.fuelInfo.heatPerSecond) : null,
                item.fuelInfo is not null && item.fuelInfo.returnItemId != null ? [new ItemsCatalog.Item.ReturnItem(item.fuelInfo.returnItemId, 1)] : [],
                item.consumeInfo != null && item.consumeInfo.returnItemId != null ? [new ItemsCatalog.Item.ReturnItem(item.consumeInfo.returnItemId, 1)] : [],
                item.experience.tappable,
                new Dictionary<string, int?>() { ["tappable"] = item.experience.tappable, ["encounter"] = item.experience.encounter, ["crafting"] = item.experience.crafting },
                false
            );
        })];

        Dictionary<string, ItemsCatalog.EfficiencyCategory> efficiencyCategories = [];
        foreach (Catalog.ItemEfficiencyCategoriesCatalog.EfficiencyCategory efficiencyCategory in catalog.itemEfficiencyCategoriesCatalog.efficiencyCategories)
        {
            efficiencyCategories[efficiencyCategory.name] = new ItemsCatalog.EfficiencyCategory(
                new ItemsCatalog.EfficiencyCategory.EfficiencyMap(
                    efficiencyCategory.hand,
                    efficiencyCategory.hoe,
                    efficiencyCategory.axe,
                    efficiencyCategory.shovel,
                    efficiencyCategory.pickaxe_1,
                    efficiencyCategory.pickaxe_2,
                    efficiencyCategory.pickaxe_3,
                    efficiencyCategory.pickaxe_4,
                    efficiencyCategory.pickaxe_5,
                    efficiencyCategory.sword,
                    efficiencyCategory.sheers
                )
            );
        }

        return new ItemsCatalog(items, efficiencyCategories);
    }

    private static RecipesCatalog makeRecipesCatalogApiResponse(Catalog catalog)
    {
        RecipesCatalog.CraftingRecipe[] crafting = [.. catalog.recipesCatalog.crafting.Select(recipe =>
        {
            string categoryString = recipe.category switch
            {
                CRCCRCategory.CONSTRUCTION => "Construction",
                CRCCRCategory.EQUIPMENT => "Equipment",
                CRCCRCategory.ITEMS => "Items",
                CRCCRCategory.NATURE => "Nature",
                _ => throw new UnreachableException(),
            };

            return new RecipesCatalog.CraftingRecipe(
                    recipe.id,
                    categoryString,
                    TimeFormatter.FormatDuration(recipe.duration * 1000),
                    [.. recipe.ingredients.Select(ingredient => new RecipesCatalog.CraftingRecipe.Ingredient(ingredient.possibleItemIds, ingredient.count))],
                    new RecipesCatalog.CraftingRecipe.Output(recipe.output.itemId, recipe.output.count),
                    [.. recipe.returnItems.Select(returnItem => new RecipesCatalog.CraftingRecipe.ReturnItem(returnItem.itemId, returnItem.count))],
                    false
            );
        })];

        RecipesCatalog.SmeltingRecipe[] smelting = [.. catalog.recipesCatalog.smelting.Select(recipe =>
        {
            return new RecipesCatalog.SmeltingRecipe(
                recipe.id,
                recipe.heatRequired,
                recipe.input,
                new RecipesCatalog.SmeltingRecipe.Output(recipe.output, 1),
                recipe.returnItemId != null ? [new RecipesCatalog.SmeltingRecipe.ReturnItem(recipe.returnItemId, 1)] : [],
                false
            );
        })];

        return new RecipesCatalog(crafting, smelting);
    }

    private static JournalCatalog makeJournalCatalogApiResponse(Catalog catalog)
    {
        Dictionary<string, JournalCatalog.Item> items = [];
        foreach (Catalog.ItemJournalGroupsCatalog.JournalGroup group in catalog.itemJournalGroupsCatalog.groups)
        {
            string parentCollectionString = group.parentCollection switch
            {
                CIJGCJGParentCollection.BLOCKS => "Blocks",
                CIJGCJGParentCollection.ITEMS_CRAFTED => "ItemsCrafted",
                CIJGCJGParentCollection.ITEMS_SMELTED => "ItemsSmelted",
                CIJGCJGParentCollection.MOBS => "Mobs",
                _ => throw new UnreachableException(),
            };

            items[group.name] = new JournalCatalog.Item(
                    group.id,
                    parentCollectionString,
                    group.order,
                    group.order,
                    group.defaultSound,
                    false,
                    "200526.173531"
            );
        }

        return new JournalCatalog(items);
    }

    private static NFCBoost[] makeNFCBoostsCatalogApiResponse(Catalog catalog)
    {
        // TODO
        return [];
    }
}
