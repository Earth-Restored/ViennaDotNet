namespace ViennaDotNet.ApiServer.Types.Inventory;

public sealed record HotbarItem(
     string Id,
     int Count,
     string? InstanceId,
     float? Health
);
