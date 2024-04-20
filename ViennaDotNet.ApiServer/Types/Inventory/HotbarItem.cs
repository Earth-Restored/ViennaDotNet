namespace ViennaDotNet.ApiServer.Types.Inventory
{
    public record HotbarItem(
         string id,
         int count,
         string? instanceId,
         float? health
    )
    {
    }
}
