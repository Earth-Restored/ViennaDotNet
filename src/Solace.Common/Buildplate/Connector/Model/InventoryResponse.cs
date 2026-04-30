namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record InventoryResponse(
    InventoryResponse.Item[] Items,
    InventoryResponse.HotbarItem?[] Hotbar
)
{
    public sealed record Item(
        string Id,
        int Count,
        string? InstanceId,
        int Wear
    );

    public sealed record HotbarItem(
        string Id,
        int Count,
        string? InstanceId
    );
}
