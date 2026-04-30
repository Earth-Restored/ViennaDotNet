namespace ViennaDotNet.ApiServer.Types.Inventory;

public sealed record NonStackableInventoryItem(
    string Id,
    NonStackableInventoryItem.Instance[] Instances,
    int Fragments,
    NonStackableInventoryItem.OnR Unlocked,
    NonStackableInventoryItem.OnR Seen
)
{
    public sealed record Instance(
        string Id,
        float Health
    );

    public sealed record OnR(
        string On
    );
}
