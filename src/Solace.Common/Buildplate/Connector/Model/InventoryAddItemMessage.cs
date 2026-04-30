namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record InventoryAddItemMessage(
     string PlayerId,
     string ItemId,
     int Count,
     string? InstanceId,
     int Wear
);