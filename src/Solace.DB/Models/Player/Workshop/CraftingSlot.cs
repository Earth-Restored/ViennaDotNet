namespace Solace.DB.Models.Player.Workshop;

public sealed class CraftingSlot : IEquatable<CraftingSlot>
{
    public ActiveJobR? ActiveJob { get; set; }
    public bool Locked { get; set; }

    public CraftingSlot()
    {
        ActiveJob = null;
        Locked = false;
    }

    public bool Equals(CraftingSlot? other)
        => other is not null && ActiveJob == other.ActiveJob && Locked == other.Locked;

    public override bool Equals(object? obj)
        => Equals(obj as CraftingSlot);

    public override int GetHashCode()
        => HashCode.Combine(ActiveJob, Locked);

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
