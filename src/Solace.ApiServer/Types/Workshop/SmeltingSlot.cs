using ViennaDotNet.ApiServer.Types.Common;

namespace ViennaDotNet.ApiServer.Types.Workshop;

public record SmeltingSlot(
    SmeltingSlot.FuelR? Fuel,
    SmeltingSlot.BurningR? Burning,
    string? SessionId,
    string? RecipeId,
    OutputItem? Output,
    InputItem[]? Escrow,
    int Completed,
    int Available,
    int Total,
    string? NextCompletionUtc,
    string? TotalCompletionUtc,
    State State,
    BoostState? BoostState,
    UnlockPrice? UnlockPrice,
    int StreamVersion
)
{
    public sealed record FuelR(
        BurnRate BurnRate,
        string ItemId,
        int Quantity,
        string[] ItemInstanceIds
    );

    public sealed record BurningR(
        string? BurnStartTime,
        string? BurnsUntil,
        string? RemainingBurnTime,
        float? HeatDepleted,
        FuelR Fuel
    );
}
