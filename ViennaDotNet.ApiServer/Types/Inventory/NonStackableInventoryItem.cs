namespace ViennaDotNet.ApiServer.Types.Inventory
{
    public record NonStackableInventoryItem(
        string id,
        NonStackableInventoryItem.Instance[] instances,
        int fragments,
        NonStackableInventoryItem.On unlocked,
        NonStackableInventoryItem.On seen
    )
    {
        public record Instance(
            string id,
            float health
        )
        {
        }

        public record On(
            string on
        )
        {
        }
    }
}
