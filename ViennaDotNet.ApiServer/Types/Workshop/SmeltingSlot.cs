using ViennaDotNet.ApiServer.Types.Common;

namespace ViennaDotNet.ApiServer.Types.Workshop
{
    public record SmeltingSlot(
        SmeltingSlot.Fuel? fuel,
        SmeltingSlot.Burning? burning,
        string? sessionId,
        string? recipeId,
        OutputItem? output,
        InputItem[]? escrow,
        int completed,
        int available,
        int total,
        string? nextCompletionUtc,
        string? totalCompletionUtc,
        State state,
        BoostState? boostState,
        UnlockPrice? unlockPrice,
        int streamVersion
    )
    {
        public record Fuel(
            BurnRate burnRate,
            string itemId,
            int quantity,
            string[] itemInstanceIds
        )
        {
        }

        public record Burning(
            string? burnStartTime,
            string? burnsUntil,
            string? remainingBurnTime,
            float? heatDepleted,
            Fuel fuel
        )
        {
        }
    }
}
