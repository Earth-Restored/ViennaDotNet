namespace ViennaDotNet.ApiServer.Types.Inventory;

public sealed record StackableInventoryItem(
    string Id,
    int Owned,
    int Fragments,
    StackableInventoryItem.OnR Unlocked,
    StackableInventoryItem.OnR Seen
)
{
    public sealed record OnR(
        string On
    );
}
