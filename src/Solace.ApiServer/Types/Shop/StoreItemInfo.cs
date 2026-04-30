using System.Text.Json.Serialization;
using Solace.ApiServer.Types.Buildplates;

namespace Solace.ApiServer.Types.Shop;

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
