using Microsoft.AspNetCore.Mvc;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using OData2Linq;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViennaDotNet.ApiServer.Models.Playfab;
using ViennaDotNet.Common.Utils;
using CItem = ViennaDotNet.StaticData.Playfab.Item;

namespace ViennaDotNet.ApiServer.Controllers.PlayfabApi;

[Route("Catalog")]
[Route("20CA2.playfabapi.com/Catalog")]
public class CatalogController : ViennaControllerBase
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new UtcDateTimeConverter() },
    };

    private static StaticData.StaticData StaticData => Program.staticData;

    private static readonly Item[] itemData;

    static CatalogController()
    {
        var brfPrice = new Item.PriceR([
            new([new("ecd19d3c-7635-402c-a185-eb11cb6c6946", "ecd19d3c-7635-402c-a185-eb11cb6c6946", "ecd19d3c-7635-402c-a185-eb11cb6c6946", 0)]),
            new([new("0113e233-7637-48e7-91b0-349fdc74713d", "0113e233-7637-48e7-91b0-349fdc74713d", "0113e233-7637-48e7-91b0-349fdc74713d", 0)])
        ], []);

        itemData = [
            // required for shop to load for some reason...
            new Item(
                new("B63A0803D3653643", "namespace", "namespace"),
                new("B63A0803D3653643", "namespace", "namespace"),
                Guid.Parse("230f5996-04b2-4f0e-83e5-4056c7f1d946"),
                "bundle",
                [new("FriendlyId", Guid.Parse("53bee6fe-c9d9-43c9-b3af-4c5438fba4b7"))],
                null,
                new() { ["en-US"] = "Bold Rabbit Feet", ["NEUTRAL"] = "Bold Rabbit Feet", ["neutral"] = "Bold Rabbit Feet", },
                new() { ["en-US"] = "§", ["NEUTRAL"] = "§", ["neutral"] = "§", },
                new() { ["en-US"] = new(["Animal"]), ["NEUTRAL"] = new(["Animal"]), ["neutral"] = new(["Animal"]), },
                "PersonaDurable",
                new("301F442C3B63DC20", "master_player_account", "master_player_account"),
                new("301F442C3B63DC20", "master_player_account", "master_player_account"),
                false, // IsStackable
                ["android.amazonappstore", "android.googleplay",  "b.store",  "ios.store",  "nx.store",  "oculus.store.gearvr", "oculus.store.rift", "uwp.store",  "uwp.store.mobile",  "xboxone.store", "title.bedrockvanilla", "title.earth"],
                ["230f5996-04b2-4f0e-83e5-4056c7f1d946", "4f7cdadd-a33c-489d-8969-752ca689f567", "is_achievement", "earth_achievement", "tag.animal", "1P"],
                new(2020, 12, 7, 22, 46, 33, 066, DateTimeKind.Utc),
                new(2023, 8, 10, 14, 11, 19, 81, DateTimeKind.Utc),
                null,
                [new Dictionary<string,object>() {
                    ["Id"] = "f4a2cf48-45c1-4fda-86d0-9d24c069f0a9",
                    ["Url"] = "https://xforgeassets001.xboxlive.com/pf-title-b63a0803d3653643-20ca2/f4a2cf48-45c1-4fda-86d0-9d24c069f0a9/primary.zip",
                    ["MaxClientVersion"] = "65535.65535.65535",
                    ["MinClientVersion"] = "1.13.0",
                    ["Tags"] = Array.Empty<string>(),
                    ["Type"] = "personabinary",
                }],
                [new("e7314d2a-8097-48f0-b0e8-039084a22049", "Thumbnail", "Thumbnail", "/playfab/images/shoes_bold_striped_rabbit_thumbnail_0.png")],
                [new(Guid.Parse("8eb22e2c-db50-4e30-a3d2-0c355e479e74"), 1)],
                brfPrice,
                brfPrice,
                [],
                Item.DisplayPropertiesR.CreatePersona(
                    "Minecraft",
                    0,
                    true,
                    "rare",
                    [new("persona_piece", Guid.Parse("4f7cdadd-a33c-489d-8969-752ca689f567"), "1.1.0"),],
                    Guid.Parse("53bee6fe-c9d9-43c9-b3af-4c5438fba4b7"),
                    "persona_feet"
                )
            ),
            .. StaticData.Playfab.Items.Select(item => CIItemToItem(item.Value)),
        ];
    }

    private sealed record CatalogSearchRequest(
        bool Count,
        string Filter,
        string? Select,
        string? OrderBy,
        int? Top,
        int? Skip,
        string Scid
    );

    [HttpPost("Search")]
    public async Task<IActionResult> SearchAsync()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<CatalogSearchRequest>(cancellationToken);

        if (request is null)
        {
            return BadRequest();
        }

        IEnumerable<Item> items;
        try
        {
            string filter = request.Filter
                .Replace("platforms/any(tp: tp eq 'android.googleplay' and tp eq 'title.earth')", "platforms/any(tp: tp eq 'android.googleplay') and platforms/any(tp: tp eq 'title.earth')");

            var oDataQuery = itemData.AsQueryable().OData(settings =>
                {
                    settings.EnableCaseInsensitive = true;
                    settings.ValidationSettings.MaxNodeCount = 10000;
                }, GetEdmModel())
                .Filter(filter);

            if (request.OrderBy is not null)
            {
                oDataQuery = oDataQuery.OrderBy(request.OrderBy);
            }

            var query = oDataQuery.ToOriginalQuery();

            if (request.Skip is { } skip)
            {
                query = query.Skip(skip);
            }

            if (request.Top is { } top)
            {
                query = query.Take(top);
            }

            items = query
                .ToArray()
                .Select(item => ToResponse(item, Request));
        }
        catch (Exception ex)
        {
            items = [];
        }

        var response = new Dictionary<string, object>();

        if (request.Count)
        {
            response["Count"] = items.Count(); // items is empty or select over array, so this is fine
        }

        response["Items"] = items;
        response["ConfigurationName"] = "DEFAULT";

        Response.Headers.Append("access-control-allow-credentials", "true");
        Response.Headers.Append("access-control-allow-headers", "Content-Type, Content-Encoding, X-Authentication, X-Authorization, X-PlayFabSDK, X-ReportErrorAsSuccess, X-SecretKey, X-EntityToken, Authorization, x-ms-app, x-ms-client-request-id, x-ms-user-id, traceparent, tracestate, Request-Id");
        Response.Headers.Append("access-control-allow-methods", "GET, POST");
        Response.Headers.Append("access-control-allow-origin", "*");

        return Content(JsonSerializer.Serialize(new PlayfabOkResponse(
            200,
            "OK",
            response
        ), jsonOptions), "application/json");
    }

    [HttpPost("SearchStores")]
    public IActionResult SearchStores()
    {
        return JsonPascalCase(new PlayfabOkResponse(
            200,
            "OK",
            new Dictionary<string, object>()
            {
                ["Count"] = 0,
                ["Stores"] = Array.Empty<object>(),
                ["ConfigurationName"] = "DEFAULT",
            }
        ));
    }

    private sealed record GetPublishedItemRequest(
        string? ItemId
    );

    private sealed record GetPublishedItemResponse(
        Item Item
    );

    [HttpPost("GetPublishedItem")]
    public async Task<IActionResult> GetPublishedItem()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<GetPublishedItemRequest>(cancellationToken);

        if (request is null)
        {
            return BadRequest();
        }

        if (!Guid.TryParse(request.ItemId, out var itemId))
        {
            return JsonPascalCase(new PlayfabErrorResponse(
                400,
                "BadRequest",
                "InvalidParams",
                1000,
                "Invalid input parameters",
                new()
                {
                    ["ItemId"] = ["The ItemId field is required."]
                }
            ));
        }

        if (!StaticData.Playfab.Items.TryGetValue(itemId, out var cItem))
        {
            // TODO: fake not found
            return NotFound();
        }

        var item = CIItemToItem(cItem);

        return Content(JsonSerializer.Serialize(new PlayfabOkResponse(
            200,
            "OK",
            new GetPublishedItemResponse(
                ToResponse(item, Request)
            )
        ), jsonOptions), "application/json");
    }

    private static Item CIItemToItem(CItem item)
    {
        Item.PriceR? price = item.Data switch
        {
            CItem.BuildplateData data => new Item.PriceR([
                new([
                    new("8b77345d-6250-4321-b3c2-373468b39457", "8b77345d-6250-4321-b3c2-373468b39457", "8b77345d-6250-4321-b3c2-373468b39457", data.Cost),
                ]),
            ], []),
            CItem.InventoryItemData data => new Item.PriceR([
                new([
                    new("8b77345d-6250-4321-b3c2-373468b39457", "8b77345d-6250-4321-b3c2-373468b39457", "8b77345d-6250-4321-b3c2-373468b39457", data.Cost),
                ]),
            ], []),
            CItem.RubyData => null,
            CItem.QueryManifestData => null,
            _ => throw new UnreachableException(),
        };

        return new Item(
            new Item.Entity(item.SourceEntityId, "namespace", "namespace"),
            new Item.Entity(item.SourceEntityId, "namespace", "namespace"),
            item.Id,
            item.Data switch
            {
                CItem.BuildplateData => "bundle",
                CItem.InventoryItemData => "bundle",
                CItem.RubyData => "catalogItem",
                CItem.QueryManifestData => "catalogItem",
                _ => throw new UnreachableException(),
            },
            item.FriendlyId is null ? [] : [new("FriendlyId", item.FriendlyId.Value)],
            item.FriendlyId,
            ((IEnumerable<KeyValuePair<string, string>>)[new("NEUTRAL", item.Title), .. item.TitleTranslations, new("neutral", item.Title)])
                .ToDictionary(),
            ((IEnumerable<KeyValuePair<string, string>>)[new("NEUTRAL", item.Description), .. item.DescriptionTranslations, new("neutral", item.Description)])
                .ToDictionary(),
            item.Keywords.ToDictionary(item => item.Key, item => new Item.KeywordValues(item.Value.Values)),
            item.Data switch
            {
                CItem.BuildplateData => "BuildplateOffer",
                CItem.InventoryItemData => "InventoryItemOffer",
                CItem.RubyData => "RubyOffer",
                CItem.QueryManifestData => "GenoaQueryManifest_V0.0.3",
                _ => throw new UnreachableException(),
            },
            new Item.Entity(item.CreatorEntityId, "title_player_account", "title_player_account"),
            new Item.Entity(item.CreatorEntityId, "title_player_account", "title_player_account"),
            item.Data is CItem.RubyData ? false : null, // IsStackable
            item.Data switch
            {
                CItem.BuildplateData => ["android.amazonappstore", "android.googleplay", "b.store", "ios.store", "nx.store", "oculus.store.gearvr", "oculus.store.rift", "uwp.store", "uwp.store.mobile", "xboxone.store", "title.bedrockvanilla", "title.earth"],
                CItem.InventoryItemData => ["android.googleplay", "ios.store", "uwp.store", "title.earth"],
                CItem.RubyData => ["android.googleplay", "ios.store", "uwp.store", "title.bedrockvanilla", "title.earth"],
                CItem.QueryManifestData => ["android.googleplay", "ios.store", "uwp.store", "title.earth"],
                _ => throw new UnreachableException(),
            },
            item.Tags,
            item.CreationDate,
            item.LastModifiedDate,
            item.StartDate,
            item.Contents.Select(content => content switch
            {
                CItem.QueryManifestContent qmContent => new Item.QueryManifestContent(qmContent.Id, qmContent.Url, qmContent.MaxClientVersion, qmContent.MinClientVersion, qmContent.Tags, qmContent.Type),
                _ => content,
            }),
            item.ThumbnailImageId is null ? [] : [new(item.ThumbnailImageId, "Thumbnail", "Thumbnail", $"/playfab/images/{item.ThumbnailImageId}.png")],
            item.ItemReferences.Select(reference => new Item.ItemReference(reference.Id, reference.Amount)),
            price,
            price,
            [],
            item.Data switch
            {
                CItem.BuildplateData data => Item.DisplayPropertiesR.CreateBuildplate(
                    "Minecraft",
                    data.Cost,
                    item.Purchasable,
                    data.Rarity.ToString().ToLowerInvariant(),
                    [new("entitlement_EarthBuildPlate", data.Id, data.Version)],
                    data.Id,
                    data.Size.ToString().ToLowerInvariant(),
                    data.UnlockLevel
                ),
                CItem.InventoryItemData data => Item.DisplayPropertiesR.CreateInventoryItem(
                    data.Cost,
                    data.Rarity.ToString(),
                    [new("entitlement_InventoryItemOffer", data.Id, data.Version)],
                    data.Id,
                    data.Amount
                ),
                CItem.RubyData data => Item.DisplayPropertiesR.CreateRuby(
                    data.BonusCoinCount,
                    data.CoinCount,
                    data.OriginalCreatorId,
                    data.Sku
                ),
                CItem.QueryManifestData data => Item.DisplayPropertiesR.CreateQueryManifest(
                    data.MinClientVersion,
                    data.MaxClientVersion,
                    data.Tabs.Select(tab => new Item.DisplayPropertiesR.Tab(
                        tab.ScreenLayoutQueries.Select(layoutQuery => new Item.DisplayPropertiesR.Tab.ScreenLayoutQuery(
                            // TODO: haven't seen it yet, but it's possible these can have properties
                            layoutQuery.ColumnType is ViennaDotNet.StaticData.Playfab.Tab.ColumnType.Rectangle ? new object() : null,
                            layoutQuery.ColumnType is ViennaDotNet.StaticData.Playfab.Tab.ColumnType.Square ? new object() : null,
                            layoutQuery.ColumnType is ViennaDotNet.StaticData.Playfab.Tab.ColumnType.Grid ? new object() : null,
                            layoutQuery.Queries.Select(query => new Item.DisplayPropertiesR.Tab.ScreenLayoutQuery.Query(
                                query.ProductIds,
                                query.QueryContentTypes.Select(type => type.ToString()),
                                query.TopCount
                            )),
                            layoutQuery.ComponentId
                        )),
                        tab.TabIcon,
                        tab.TabTitle,
                        tab.TabId
                    )),
                    data.GlobalNotSearchQueryTags
                ),
                _ => throw new UnreachableException(),
            }
        );
    }

    private static Item ToResponse(Item item, HttpRequest request)
    {
        string host = $"{(request.IsHttps ? "https://" : "http://")}{request.Host.Value}";

        return item with
        {
            Images = item.Images.Select(image => image with { Url = host + image.Url, }),
            Contents = item.Contents.Select(content => content switch
            {
                Item.QueryManifestContent qmContent => qmContent with { Url = host + qmContent.Url, },
                _ => content,
            }),
        };
    }

    public static IEdmModel GetEdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.EntitySet<Item>("Item");

        builder.ComplexType<Item.Entity>();
        builder.ComplexType<Item.AlternateId>();
        builder.ComplexType<Item.KeywordValues>();
        builder.ComplexType<Item.Image>();
        builder.ComplexType<Item.ItemReference>();
        builder.ComplexType<Item.PriceR>();
        builder.ComplexType<Item.PriceR.Price>();
        builder.ComplexType<Item.CurrencyAmount>();
        builder.ComplexType<Item.DisplayPropertiesR>();
        builder.ComplexType<Item.DisplayPropertiesR.Tab>();
        builder.ComplexType<Item.DisplayPropertiesR.Tab.ScreenLayoutQuery>();
        builder.ComplexType<Item.DisplayPropertiesR.Tab.ScreenLayoutQuery.Query>();

        return builder.GetEdmModel();
    }

    private sealed record Item(
        Item.Entity SourceEntity,
        Item.Entity SourceEntityKey,
        Guid Id,
        string Type,
        IEnumerable<Item.AlternateId> AlternateIds,
        Guid? FriendlyId,
        Dictionary<string, string> Title,
        Dictionary<string, string> Description,
        Dictionary<string, Item.KeywordValues> Keywords,
        string ContentType,
        Item.Entity CreatorEntityKey,
        Item.Entity CreatorEntity,
        bool? IsStackable, // TODO: ??? only used for ruby offer, always false
        IEnumerable<string> Platforms,
        IEnumerable<string> Tags,
        DateTime CreationDate,
        DateTime LastModifiedDate,
        DateTime? StartDate,
        IEnumerable<object> Contents,
        IEnumerable<Item.Image> Images,
        IEnumerable<Item.ItemReference> ItemReferences,
        Item.PriceR? Price,
        Item.PriceR? PriceOptions,
        IEnumerable<object> DeepLinks,
        Item.DisplayPropertiesR DisplayProperties,
        string? ETag = null
    )
    {
        public sealed record Entity(
            string Id,
            string Type,
            string TypeString
        );

        public sealed record AlternateId(
            string Type,
            Guid Value
        );

        public sealed record KeywordValues(
            IEnumerable<string> Values
        );

        public sealed record Image(
            string Id,
            string Tag,
            string Type,
            string Url
        );

        public sealed record ItemReference(
            Guid Id,
            int Amount
        );

        public sealed record PriceR(
            PriceR.Price[] Prices,
            PriceR.Price[] RealPrices
        )
        {
            public sealed record Price(
                CurrencyAmount[] Amounts
            );
        }

        public sealed record CurrencyAmount(
            string CurrencyId,
            string Id,
            string ItemId,
            int Amount
        );

        public sealed record QueryManifestContent(
            string Id,
            string Url,
            string MaxClientVersion,
            string MinClientVersion,
            IEnumerable<string> Tags,
            string Type
        );

        public sealed record PackIdentity(
            [property: JsonPropertyName("type")] string Type,
            [property: JsonPropertyName("uuid")] Guid Uuid,
            [property: JsonPropertyName("version")] string Version
        );

        public sealed record DisplayPropertiesR(
            // query manifest
            [property: JsonPropertyName("minClientVersion")] string? MinClientVersion = null,
            [property: JsonPropertyName("maxClientVersion")] string? MaxClientVersion = null,
            [property: JsonPropertyName("tabs")] IEnumerable<DisplayPropertiesR.Tab>? Tabs = null,
            [property: JsonPropertyName("globalNotSearchQueryTags")] IEnumerable<string>? GlobalNotSearchQueryTags = null,

            // buildplate, inventory item, persona
            [property: JsonPropertyName("price")] int? Price = null,
            [property: JsonPropertyName("rarity")] string? Rarity = null,
            [property: JsonPropertyName("packIdentity")] IEnumerable<PackIdentity>? PackIdentity = null,

            // buildplate, persona
            [property: JsonPropertyName("creatorName")] string? CreatorName = null,
            [property: JsonPropertyName("purchasable")] bool? Purchasable = null,

            // buildplate
            [property: JsonPropertyName("buildPlateId")] Guid? BuildPlateId = null,
            [property: JsonPropertyName("buildPlateSize")] string? BuildPlateSize = null,
            [property: JsonPropertyName("buildPlateUnlockLevel"), JsonNumberHandling(JsonNumberHandling.WriteAsString)] int? BuildPlateUnlockLevel = null,

            // inventory item
            [property: JsonPropertyName("itemId")] Guid? ItemId = null,
            [property: JsonPropertyName("amount")] int? Amount = null,

            // ruby
            [property: JsonPropertyName("bonusCoinCount")] int? BonusCoinCount = null,
            [property: JsonPropertyName("coinCount")] int? CoinCount = null,
            [property: JsonPropertyName("originalCreatorId")] string? OriginalCreatorId = null,
            [property: JsonPropertyName("sku")] string? Sku = null,

            // persona
            [property: JsonPropertyName("offerId")] Guid? OfferId = null,
            [property: JsonPropertyName("pieceType")] string? PieceType = null
        )
        {
            public static DisplayPropertiesR CreateQueryManifest(string minClientVersion, string maxClientVersion, IEnumerable<Tab> tabs, IEnumerable<string> globalNotSearchQueryTags)
                => new DisplayPropertiesR(MinClientVersion: minClientVersion, MaxClientVersion: maxClientVersion, Tabs: tabs, GlobalNotSearchQueryTags: globalNotSearchQueryTags);

            public static DisplayPropertiesR CreateBuildplate(string creatorName, int price, bool purchasable, string rarity, IEnumerable<PackIdentity> packIdentity, Guid buildPlateId, string buildPlateSize, int buildPlateUnlockLevel)
                => new DisplayPropertiesR(CreatorName: creatorName, Price: price, Purchasable: purchasable, Rarity: rarity, PackIdentity: packIdentity, BuildPlateId: buildPlateId, BuildPlateSize: buildPlateSize, BuildPlateUnlockLevel: buildPlateUnlockLevel);

            public static DisplayPropertiesR CreateInventoryItem(int price, string rarity, IEnumerable<PackIdentity> packIdentity, Guid itemId, int amount)
                => new DisplayPropertiesR(Price: price, Rarity: rarity, PackIdentity: packIdentity, ItemId: itemId, Amount: amount);

            public static DisplayPropertiesR CreateRuby(int? bonusCoinCount, int coinCount, string originalCreatorId, string sku)
                => new DisplayPropertiesR(BonusCoinCount: bonusCoinCount, CoinCount: coinCount, OriginalCreatorId: originalCreatorId, Sku: sku);

            public static DisplayPropertiesR CreatePersona(string creatorName, int price, bool purchasable, string rarity, IEnumerable<PackIdentity> packIdentity, Guid offerId, string pieceType)
                => new DisplayPropertiesR(CreatorName: creatorName, Price: price, Purchasable: purchasable, Rarity: rarity, PackIdentity: packIdentity, OfferId: offerId, PieceType: pieceType);

            public sealed record Tab(
              [property: JsonPropertyName("screenLayoutQueries")] IEnumerable<Tab.ScreenLayoutQuery> ScreenLayoutQueries,
              [property: JsonPropertyName("tabIcon")] string TabIcon,
              [property: JsonPropertyName("tabTitle")] string TabTitle,
              [property: JsonPropertyName("tabId")] string TabId
          )
            {
                public sealed record ScreenLayoutQuery(
                    [property: JsonPropertyName("column_rectangle")] object? ColumnRectangle,
                    [property: JsonPropertyName("column_square")] object? ColumnSquare,
                    [property: JsonPropertyName("column_grid")] object? ColumnGrid,
                    [property: JsonPropertyName("queries")] IEnumerable<ScreenLayoutQuery.Query> Queries,
                    [property: JsonPropertyName("componentId")] Guid ComponentId
                )
                {
                    public sealed record Query(
                        [property: JsonPropertyName("productIds")] IEnumerable<string> ProductIds,
                        [property: JsonPropertyName("queryContentTypes")] IEnumerable<string> QueryContentTypes,
                        [property: JsonPropertyName("topCount")] int TopCount
                    );
                }
            }
        }
    }
}
