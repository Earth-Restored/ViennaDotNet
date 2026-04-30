namespace ViennaDotNet.DB.Models.Player.Workshop;

public sealed class CraftingSlot
{
    public ActiveJobR? ActiveJob { get; set; }
    public bool Locked { get; set; }

    public CraftingSlot()
    {
        ActiveJob = null;
        Locked = false;
    }

    public sealed record ActiveJobR(
        string SessionId,
        string RecipeId,
        long StartTime,
        InputItem[][] Input,
        int TotalRounds,
        int CollectedRounds,
        bool FinishedEarly
    );
}
