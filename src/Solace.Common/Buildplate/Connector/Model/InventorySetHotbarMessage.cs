namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record InventorySetHotbarMessage(
    string PlayerId,
    InventorySetHotbarMessage.Item[] Items
)
{
    public sealed record Item(
        string ItemId,
        int Count,
        string? InstanceId
    );
}
