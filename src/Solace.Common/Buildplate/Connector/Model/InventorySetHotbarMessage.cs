#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Solace.Buildplate.Connector.Model;
#pragma warning restore IDE0130 // Namespace does not match folder structure

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
