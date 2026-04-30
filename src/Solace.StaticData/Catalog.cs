using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using ViennaDotNet.Common;

namespace ViennaDotNet.StaticData;

public sealed class Catalog
{
    public readonly ItemsCatalogR ItemsCatalog;
    public readonly ItemEfficiencyCategoriesCatalogR ItemEfficiencyCategoriesCatalog;
    public readonly ItemJournalGroupsCatalogR ItemJournalGroupsCatalog;
    public readonly RecipesCatalogR RecipesCatalog;
    public readonly NFCBoostsCatalogR NfcBoostsCatalog;

    internal Catalog(string dir)
    {
        try
        {
            ItemsCatalog = new ItemsCatalogR(Path.Combine(dir, "items.json"));
            ItemEfficiencyCategoriesCatalog = new ItemEfficiencyCategoriesCatalogR(Path.Combine(dir, "itemEfficiencyCategories.json"));
            ItemJournalGroupsCatalog = new ItemJournalGroupsCatalogR(Path.Combine(dir, "itemJournalGroups.json"));
            RecipesCatalog = new RecipesCatalogR(Path.Combine(dir, "recipes.json"));
            NfcBoostsCatalog = new NFCBoostsCatalogR(Path.Combine(dir, "nfc.json"));
        }
        catch (StaticDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StaticDataException(null, exception);
        }
    }

    public sealed class ItemsCatalogR
    {
        public readonly ImmutableArray<Item> Items;

        private readonly Dictionary<string, Item> itemsById = [];

        internal ItemsCatalogR(string file)
        {
            using (var stream = File.OpenRead(file))
            {
                Item[]? items = Json.Deserialize<Item[]>(stream);

                Debug.Assert(items is not null);

                Items = ImmutableCollectionsMarshal.AsImmutableArray(items);
            }

            HashSet<string> ids = [];
            HashSet<string> names = [];
            foreach (Item item in Items)
            {
                if (!ids.Add(item.Id))
                {
                    throw new StaticDataException($"Duplicate item ID {item.Id}");
                }

                if (!names.Add(item.Name + "." + item.Aux))
                {
                    throw new StaticDataException($"Duplicate item name/aux {item.Name} {item.Aux}");
                }
            }

            foreach (Item item in Items)
            {
                itemsById[item.Id] = item;
            }
        }

        public Item? GetItem(string id)
            => itemsById.GetValueOrDefault(id);

        public record Item(
            string Id,
            string Name,
            int Aux,
            bool Stackable,
            Item.TypeE Type,
            Item.CategoryE Category,
            Item.RarityE Rarity,
            Item.UseTypeE UseType,
            Item.UseTypeE AlternativeUseType,
            Item.BlockInfoR? BlockInfo,
            Item.ToolInfoR? ToolInfo,
            Item.ConsumeInfoR? ConsumeInfo,
            Item.FuelInfoR? FuelInfo,
            Item.ProjectileInfoR? ProjectileInfo,
            Item.MobInfoR? MobInfo,
            Item.BoostInfoR? BoostInfo,
            Item.JournalEntryR? JournalEntry,
            Item.ExperienceR Experience
        )
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum TypeE
            {
                BLOCK,
                ITEM,
                TOOL,
                MOB,
                ENVIRONMENT_BLOCK,
                BOOST,
                ADVENTURE_SCROLL
            }

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum CategoryE
            {
                CONSTRUCTION,
                EQUIPMENT,
                ITEMS,
                MOBS,
                NATURE,
                BOOST_ADVENTURE_XP,
                BOOST_CRAFTING,
                BOOST_DEFENSE,
                BOOST_EATING,
                BOOST_HEALTH,
                BOOST_HOARDING,
                BOOST_ITEM_XP,
                BOOST_MINING_SPEED,
                BOOST_RETENTION,
                BOOST_SMELTING,
                BOOST_STRENGTH,
                BOOST_TAPPABLE_RADIUS
            }

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum RarityE
            {
                COMMON,
                UNCOMMON,
                RARE,
                EPIC,
                LEGENDARY,
                OOBE
            }

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum UseTypeE
            {
                NONE,
                BUILD,
                BUILD_ATTACK,
                INTERACT,
                INTERACT_AND_BUILD,
                DESTROY,
                USE,
                CONSUME
            }

            public sealed record BlockInfoR(
                int BreakingHealth,
                string? EfficiencyCategory
            );

            public sealed record ToolInfoR(
                int BlockDamage,
                int MobDamage,
                int MaxWear,
                string? EfficiencyCategory
            );

            public sealed record ConsumeInfoR(
                int Heal,
                string? ReturnItemId
            );

            public sealed record FuelInfoR(
                int BurnTime,
                int HeatPerSecond,
                string? ReturnItemId
            );

            public sealed record ProjectileInfoR(
                int MobDamage
            );

            public sealed record MobInfoR(
                int Health
            );

            public sealed record BoostInfoR(
                string Name,
                int? Level,
                BoostInfoR.TypeE Type,
                bool CanBeRemoved,
                long Duration,
                bool TriggeredOnDeath,
                BoostInfoR.Effect[] Effects
            )
            {
                [JsonConverter(typeof(JsonStringEnumConverter))]
                public enum TypeE
                {
                    POTION,
                    INVENTORY_ITEM
                }

                public record Effect(
                    Effect.TypeE Type,
                    int Value,
                    string[] ApplicableItemIds,
                    Effect.ActivationE Activation
                )
                {
                    [JsonConverter(typeof(JsonStringEnumConverter))]
                    public enum TypeE
                    {
                        ADVENTURE_XP,
                        CRAFTING,
                        DEFENSE,
                        EATING,
                        HEALING,
                        HEALTH,
                        ITEM_XP,
                        MINING_SPEED,
                        RETENTION_BACKPACK,
                        RETENTION_HOTBAR,
                        RETENTION_XP,
                        SMELTING,
                        STRENGTH,
                        TAPPABLE_RADIUS
                    }

                    [JsonConverter(typeof(JsonStringEnumConverter))]
                    public enum ActivationE
                    {
                        INSTANT,
                        TIMED,
                        TRIGGERED
                    }
                }
            }

            public sealed record JournalEntryR(
                string Group,
                int Order,
                JournalEntryR.BiomeE Biome,
                JournalEntryR.BehaviorE Behavior,
                string? Sound
            )
            {
                [JsonConverter(typeof(JsonStringEnumConverter))]
                public enum BiomeE
                {
                    NONE,
                    OVERWORLD,
                    NETHER,
                    BIRCH_FOREST,
                    DESERT,
                    FLOWER_FOREST,
                    FOREST,
                    ICE_PLAINS,
                    JUNGLE,
                    MESA,
                    MUSHROOM_ISLAND,
                    OCEAN,
                    PLAINS,
                    RIVER,
                    ROOFED_FOREST,
                    SAVANNA,
                    SUNFLOWER_PLAINS,
                    SWAMP,
                    TAIGA,
                    WARM_OCEAN
                }

                [JsonConverter(typeof(JsonStringEnumConverter))]
                public enum BehaviorE
                {
                    NONE,
                    PASSIVE,
                    HOSTILE,
                    NEUTRAL
                }
            }

            public sealed record ExperienceR(
                int Tappable,
                int Encounter,
                int Crafting,
                int Journal    // TODO: what is this used for?
            );
        }
    }

    public sealed class ItemEfficiencyCategoriesCatalogR
    {
        public readonly ImmutableArray<EfficiencyCategory> EfficiencyCategories;

        internal ItemEfficiencyCategoriesCatalogR(string file)
        {
            using (var stream = File.OpenRead(file))
            {
                EfficiencyCategory[]? efficiencyCategories = Json.Deserialize<EfficiencyCategory[]>(stream);

                Debug.Assert(efficiencyCategories is not null);

                EfficiencyCategories = ImmutableCollectionsMarshal.AsImmutableArray(efficiencyCategories);
            }

            HashSet<string> names = [];
            foreach (EfficiencyCategory efficiencyCategory in EfficiencyCategories)
            {
                if (!names.Add(efficiencyCategory.Name))
                {
                    throw new StaticDataException($"Duplicate efficiency category name {efficiencyCategory.Name}");
                }
            }
        }

        public sealed record EfficiencyCategory(
            string Name,
            float Hand,
            float Hoe,
            float Axe,
            float Shovel,
            [property: JsonPropertyName("pickaxe_1")] float Pickaxe_1,
            [property: JsonPropertyName("pickaxe_2")] float Pickaxe_2,
            [property: JsonPropertyName("pickaxe_3")] float Pickaxe_3,
            [property: JsonPropertyName("pickaxe_4")] float Pickaxe_4,
            [property: JsonPropertyName("pickaxe_5")] float Pickaxe_5,
            float Sword,
            float Sheers
        );
    }

    public sealed class ItemJournalGroupsCatalogR
    {
        public readonly ImmutableArray<JournalGroup> Groups;

        internal ItemJournalGroupsCatalogR(string file)
        {
            using (var stream = File.OpenRead(file))
            {
                JournalGroup[]? groups = Json.Deserialize<JournalGroup[]>(File.ReadAllText(file));

                Debug.Assert(groups is not null);
                Groups = ImmutableCollectionsMarshal.AsImmutableArray(groups);
            }

            HashSet<string> ids = [];
            HashSet<string> names = [];
            foreach (JournalGroup journalGroup in Groups)
            {
                if (!ids.Add(journalGroup.Id))
                {
                    throw new StaticDataException($"Duplicate journal group ID {journalGroup.Id}");
                }

                if (!names.Add(journalGroup.Name))
                {
                    throw new StaticDataException($"Duplicate journal group name {journalGroup.Name}");
                }
            }
        }

        public record JournalGroup(
            string Id,
            string Name,
            JournalGroup.ParentCollectionE ParentCollection,
            int Order,
            string? DefaultSound
        )
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum ParentCollectionE
            {
                BLOCKS,
                ITEMS_CRAFTED,
                ITEMS_SMELTED,
                MOBS
            }
        }
    }

    public sealed class RecipesCatalogR
    {
        public readonly ImmutableArray<CraftingRecipe> Crafting;
        public readonly ImmutableArray<SmeltingRecipe> Smelting;

        private readonly Dictionary<string, CraftingRecipe> craftingRecipesById = [];
        private readonly Dictionary<string, SmeltingRecipe> smeltingRecipesById = [];

        private sealed record RecipesCatalogFile(
            CraftingRecipe[] Crafting,
            SmeltingRecipe[] Smelting
        );

        internal RecipesCatalogR(string file)
        {
            RecipesCatalogFile? recipesCatalogFile;
            using (var stream = File.OpenRead(file))
            {
                recipesCatalogFile = Json.Deserialize<RecipesCatalogFile>(stream);
            }

            Debug.Assert(recipesCatalogFile is not null);

            Crafting = ImmutableCollectionsMarshal.AsImmutableArray(recipesCatalogFile.Crafting);
            Smelting = ImmutableCollectionsMarshal.AsImmutableArray(recipesCatalogFile.Smelting);

            HashSet<string> craftingIds = [];
            HashSet<string> smeltingIds = [];
            foreach (CraftingRecipe craftingRecipe in Crafting)
            {
                if (!craftingIds.Add(craftingRecipe.Id))
                {
                    throw new StaticDataException($"Duplicate crafting recipe ID {craftingRecipe.Id}");
                }
            }

            foreach (SmeltingRecipe smeltingRecipe in Smelting)
            {
                if (!smeltingIds.Add(smeltingRecipe.Id))
                {
                    throw new StaticDataException($"Duplicate smelting recipe ID {smeltingRecipe.Id}");
                }
            }

            foreach (CraftingRecipe craftingRecipe in Crafting)
            {
                craftingRecipesById[craftingRecipe.Id] = craftingRecipe;
            }

            foreach (SmeltingRecipe smeltingRecipe in Smelting)
            {
                smeltingRecipesById[smeltingRecipe.Id] = smeltingRecipe;
            }
        }

        public CraftingRecipe? GetCraftingRecipe(string id)
            => craftingRecipesById.GetValueOrDefault(id);

        public SmeltingRecipe? GetSmeltingRecipe(string id)
            => smeltingRecipesById.GetValueOrDefault(id);

        public sealed record CraftingRecipe(
            string Id,
            int Duration,
            CraftingRecipe.CategoryE Category,
            CraftingRecipe.Ingredient[] Ingredients,
            CraftingRecipe.OutputR Output,
            CraftingRecipe.ReturnItem[] ReturnItems
        )
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum CategoryE
            {
                CONSTRUCTION,
                EQUIPMENT,
                ITEMS,
                NATURE
            }

            public sealed record Ingredient(
                int Count,
                string[] PossibleItemIds
            );

            public record OutputR(
                string ItemId,
                int Count
            );

            public record ReturnItem(
                string ItemId,
                int Count
            );
        }

        public sealed record SmeltingRecipe(
            string Id,
            int HeatRequired,
            string Input,
            string Output,
            string ReturnItemId
        );
    }

    public sealed class NFCBoostsCatalogR
    {
        private sealed record NFCBoostsCatalogFile(
        // TODO
        );

        internal NFCBoostsCatalogR(string file)
        {
            NFCBoostsCatalogFile? nfcBoostsCatalogFile;
            using (var stream = File.OpenRead(file))
            {
                nfcBoostsCatalogFile = Json.Deserialize<NFCBoostsCatalogFile>(stream);
            }

            // TODO
        }

        public sealed record BoostInfo
        {

        }
    }
}
