using System.Text.Json.Serialization;
using ViennaDotNet.ApiServer.Types.Buildplates;

namespace ViennaDotNet.ApiServer.Types.Shop;

public sealed record StoreItemInfo(
    Guid Id,
    StoreItemInfo.StoreItemTypeE StoreItemType,
    StoreItemInfo.StoreItemStatus? Status,
    uint StreamVersion,
    string? Model,
    Offset? BuildplateWorldOffset,
    Dimension? BuildplateWorldDimension,
    IReadOnlyDictionary<Guid, int>? InventoryCounts,
    Guid? FeaturedItem
)
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StoreItemTypeE
    {
        Buildplates,
        Items
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StoreItemStatus
    {
        Found,
        NotFound,
        NotModified
    }
}
