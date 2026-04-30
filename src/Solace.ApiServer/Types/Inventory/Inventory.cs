namespace ViennaDotNet.ApiServer.Types.Inventory;

public sealed record Inventory(
    HotbarItem?[] Hotbar,
    StackableInventoryItem[] StackableItems,
    NonStackableInventoryItem[] NonStackableItems
);