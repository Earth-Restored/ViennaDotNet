namespace ViennaDotNet.ApiServer.Types.Inventory
{
    public record StackableInventoryItem(
        string id,
        int owned,
        int fragments,
        StackableInventoryItem.On unlocked,
        StackableInventoryItem.On seen
    )
    {
        public record On(
            string on
        )
        {
        }
    }
}
