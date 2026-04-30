using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace ViennaDotNet.StaticData;

public sealed class Playfab
{
    public readonly FrozenDictionary<Guid, Item> Items;

    public readonly ImmutableArray<Tab> ShopTabs;

    public readonly ImmutableArray<string> ShopNotSearchQueryTags;

    internal Playfab(string dir)
    {
        try
        {
            var shopTabs = ImmutableArray.CreateBuilder<Tab>(2);
            foreach (string file in Directory.EnumerateFiles(Path.Combine(dir, "shop_tabs"))
                .OrderBy(file => Path.GetFileName(file)))
            {
                if (Path.GetExtension(file) != ".json")
                {
                    continue;
                }

                using (var stream = File.OpenRead(file))
                {
                    var tab = JsonSerializer.Deserialize<Tab>(stream);

                    Debug.Assert(tab is not null);

                    shopTabs.Add(tab);
                }
            }

            ShopTabs = shopTabs.DrainToImmutable();

            ShopNotSearchQueryTags = [.. File.ReadLines(Path.Combine(dir, "shop_not_search_query_tags.txt"))
                .Where(line => !string.IsNullOrWhiteSpace(line) && line.Length > 0)];

            LinkedList<Item> items = [];
            foreach (string file in Directory.EnumerateFiles(Path.Combine(dir, "items")))
            {
                if (Path.GetExtension(file) != ".json")
                {
                    continue;
                }

                using (var stream = File.OpenRead(file))
                {
                    var item = JsonSerializer.Deserialize<Item>(stream);

                    Debug.Assert(item is not null);

                    items.AddLast(item);
                }
            }

            items.AddLast(new Item(
                true,
                new Item.QueryManifestData(
                    "0.25.0",
                    "1.0.20",
                    ShopTabs,
                    ShopNotSearchQueryTags
                ),
                "Home L1",
                "Home L1",
                null,
                new(2020, 12, 10, 18, 59, 39, 396, DateTimeKind.Utc),
                new(2021, 1, 4, 19, 42, 53, 773, DateTimeKind.Utc),
                new(2021, 1, 5, 17, 0, 0, DateTimeKind.Utc),
                Guid.Parse("06e44b91-e7f5-46b6-9986-ca755890f3bf"),
                null,
                "B63A0803D3653643",
                "3C0BE9326354CBB7",
                ["mctestdefault"],
                new Dictionary<string, Item.KeywordValues>() { ["en-US"] = new([]), ["NEUTRAL"] = new([]), ["neutral"] = new([]), },
                [new Item.QueryManifestContent(
                   "f3f2b4fc-f144-4357-9e41-198db3a47957",
                    "/playfab/master_loc_contents.json",
                     "6555.6555.6555",
                     "1.2.0",
                     Array.Empty<string>(),
                     "resourcebinary"
                )],
                [],
                new Dictionary<string, string>() { ["en-US"] = "Home L1" },
                new Dictionary<string, string>() { ["en-US"] = "Home L1" }
            ));

            Items = items.ToFrozenDictionary(item => item.Id);
        }
        catch (Exception exception)
        {
            throw new StaticDataException(null, exception);
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ContentType
    {
        Durable,
        Collection,
        Bundle,
        Persona,
        Genoa,
        BuildplateOffer,
        RubyOffer,
        InventoryItemOffer,
    }

    public sealed record Item(
        bool Purchasable,
        Item.ItemData Data,
        string Title,
        string Description,
        string? ThumbnailImageId,
        DateTime CreationDate,
        DateTime LastModifiedDate,
        DateTime StartDate,
        Guid Id,
        Guid? FriendlyId,
        string SourceEntityId,
        string CreatorEntityId,
        IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, Item.KeywordValues> Keywords,
        IReadOnlyList<object> Contents,
        IReadOnlyList<Item.ItemReference> ItemReferences,
        IReadOnlyDictionary<string, string> TitleTranslations,
        IReadOnlyDictionary<string, string> DescriptionTranslations
    )
    {
        public sealed record KeywordValues(
            IReadOnlyList<string> Values
        );

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum Rarity
        {
            None,
            Common,
            Uncommon,
            Rare,
            Epic,
            Legendary,
        }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum BuidplateSize
        {
            Small,
            Medium,
            Large,
        }

        public sealed record QueryManifestContent(
            string Id,
            string Url,
            string MaxClientVersion,
            string MinClientVersion,
            IReadOnlyList<string> Tags,
            string Type
        );

        [JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
        [JsonDerivedType(typeof(BuildplateData), "Buildplate")]
        [JsonDerivedType(typeof(InventoryItemData), "InventoryItem")]
        [JsonDerivedType(typeof(RubyData), "Ruby")]
        public abstract record ItemData
        {
        }

        public sealed record BuildplateData(
            Guid Id,
            int Cost,
            BuidplateSize Size,
            int UnlockLevel,
            Rarity Rarity,
            string Version
        ) : ItemData;

        public sealed record InventoryItemData(
            Guid Id,
            int Cost,
            int Amount,
            Rarity Rarity,
            string Version
        ) : ItemData;

        public sealed record RubyData(
            int CoinCount,
            int? BonusCoinCount,
            string Sku,
            string OriginalCreatorId
        ) : ItemData;

        // does not have type discriminator, so cannot be loaded from items, instead created manually from shop tabs
        public sealed record QueryManifestData(
            string MinClientVersion,
            string MaxClientVersion,
            IReadOnlyList<Tab> Tabs,
            IReadOnlyList<string> GlobalNotSearchQueryTags
        ) : ItemData;

        public sealed record ItemReference(
            Guid Id,
            int Amount
        );
    }

    public sealed record Tab(
        string TabId,
        string TabTitle,
        string TabIcon,
        IReadOnlyList<Tab.ScreenLayoutQuery> ScreenLayoutQueries
    )
    {
        public sealed record ScreenLayoutQuery(
            ColumnType ColumnType,
            IReadOnlyList<Query> Queries,
            Guid ComponentId
        );

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum ColumnType
        {
            Rectangle,
            Square,
            Grid,
        }

        public sealed record Query(
            IReadOnlyList<string> ProductIds,
            IReadOnlyList<ContentType> QueryContentTypes,
            int TopCount
        );
    }
}
