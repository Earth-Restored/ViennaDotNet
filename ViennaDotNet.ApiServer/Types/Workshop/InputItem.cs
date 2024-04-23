namespace ViennaDotNet.ApiServer.Types.Workshop
{
    public record InputItem(
         string itemId,
         int quantity,
         string[] instanceIds
    )
    {
    }
}
