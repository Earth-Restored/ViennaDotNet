#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Solace.Buildplate.Connector.Model;
#pragma warning restore IDE0130 // Namespace does not match folder structure

public sealed record InventoryUpdateItemWearMessage(
    string PlayerId,
    string ItemId,
    string InstanceId,
    int Wear
);
