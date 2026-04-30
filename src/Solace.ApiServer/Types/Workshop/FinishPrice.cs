namespace ViennaDotNet.ApiServer.Types.Workshop;

public sealed record FinishPrice(
    int Cost,
    int Discount,
    string ValidTime
);
