namespace ViennaDotNet.ApiServer.Types.Workshop
{
    public record FinishPrice(
        int cost,
        int discount,
        string validTime
    )
    {
    }
}
