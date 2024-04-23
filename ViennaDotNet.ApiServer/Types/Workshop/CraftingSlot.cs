namespace ViennaDotNet.ApiServer.Types.Workshop
{
    public record CraftingSlot(
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
    }
}
