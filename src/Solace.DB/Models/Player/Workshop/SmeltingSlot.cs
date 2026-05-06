namespace Solace.DB.Models.Player.Workshop;

public sealed class SmeltingSlot : IEquatable<SmeltingSlot>
{
    public ActiveJobR? ActiveJob { get; set; }

    public BurningR? Burning { get; set; }

    public bool Locked { get; set; }

    public SmeltingSlot()
    {
        ActiveJob = null;
        Burning = null;
        Locked = false;
    }

    public bool Equals(SmeltingSlot? other)
        => other is not null && ActiveJob == other.ActiveJob && Burning == other.Burning && Locked == other.Locked;

    public override bool Equals(object? obj)
        => Equals(obj as SmeltingSlot);

    public override int GetHashCode()
        => HashCode.Combine(ActiveJob, Burning, Locked);

    public sealed record ActiveJobR(
        string SessionId,
        string RecipeId,
        long StartTime,
        InputItem Input,
        Fuel? AddedFuel,
        int TotalRounds,
        int CollectedRounds,
        bool FinishedEarly
    );

    public sealed record Fuel(
        InputItem Item,
        int BurnDuration,
        int HeatPerSecond
    );

    public sealed record BurningR(
        Fuel Fuel,
        int RemainingHeat
    );
}
