namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record InventoryRemoveItemRequest(
     string PlayerId,
     string ItemId,
     int Count,
     string? InstanceId
);
