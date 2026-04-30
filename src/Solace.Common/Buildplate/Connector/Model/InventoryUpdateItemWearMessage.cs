namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record InventoryUpdateItemWearMessage(
    string PlayerId,
    string ItemId,
    string InstanceId,
    int Wear
);
