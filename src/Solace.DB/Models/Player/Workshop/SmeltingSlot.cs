namespace ViennaDotNet.DB.Models.Player.Workshop;

public sealed class SmeltingSlot
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
