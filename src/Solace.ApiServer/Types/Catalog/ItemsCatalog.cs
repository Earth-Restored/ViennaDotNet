using System.Collections;
using Solace.ApiServer.Types.Common;
using static Solace.ApiServer.Types.Catalog.ItemsCatalog;
using static Solace.ApiServer.Types.Catalog.ItemsCatalog.EfficiencyCategory;
using static Solace.ApiServer.Types.Catalog.ItemsCatalog.ItemR;
using static Solace.ApiServer.Types.Catalog.ItemsCatalog.ItemR.ItemData;

namespace Solace.ApiServer.Types.Catalog;

public sealed record ItemsCatalog(
    ItemR[] Items,
    Dictionary<string, EfficiencyCategory> EfficiencyCategories
)
{
    public sealed record ItemR(
        string Id,
        ItemData Item,
        string Category,
        Rarity Rarity,
        int FragmentsRequired,
        bool Stacks,
        BurnRate? BurnRate,
        ReturnItem[] FuelReturnItems,
        ReturnItem[] ConsumeReturnItems,
        int? Experience,
        Dictionary<string, int?> ExperiencePoints,
        bool Deprecated
    )
    {
        public sealed record ItemData(
            string Name,
            int? Aux,
            string Type,
            string UseType,
            double? TapSpeed,
            double? Heal,
            double? Nutrition,
            double? MobDamage,
            double? BlockDamage,
            double? Health,
            BlockMetadataR? BlockMetadata,
            ItemMetadataR? ItemMetadata,
            BoostMetadata? BoostMetadata,
            JournalMetadataR? JournalMetadata,
            AudioMetadataR? AudioMetadata,
            IDictionary ClientProperties
        )
        {
            public sealed record BlockMetadataR(
                double? Health,
                string? EfficiencyCategory
            );

            public sealed record ItemMetadataR(
                string UseType,
                string AlternativeUseType,
                double? MobDamage,
                double? BlockDamage,
                double? WeakDamage,
                double? Nutrition,
                double? Heal,
                string? EfficiencyType,
                double? MaxHealth
            );

            public sealed record JournalMetadataR(
                string GroupKey,
                int Experience,
                int Order,
                string Behavior,
                string Biome
            );

            public sealed record AudioMetadataR(
                Dictionary<string, string> Sounds,
                string DefaultSound
            );
        }

        public sealed record ReturnItem(
            string Id,
            int Amount
        );
    }

    public sealed record EfficiencyCategory(
        EfficiencyMapR EfficiencyMap
    )
    {
        public sealed record EfficiencyMapR(
            float Hand,
            float Hoe,
            float Axe,
            float Shovel,
#pragma warning disable CA1707 // Identifiers should not contain underscores
            float Pickaxe_1,
            float Pickaxe_2,
            float Pickaxe_3,
            float Pickaxe_4,
            float Pickaxe_5,
#pragma warning restore CA1707 // Identifiers should not contain underscores
            float Sword,
            float Sheers
        );
    }
}
