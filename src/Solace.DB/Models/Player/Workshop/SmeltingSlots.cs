namespace ViennaDotNet.DB.Models.Player.Workshop;

public sealed class SmeltingSlots
{
    public SmeltingSlot[] Slots { get; init; }

    public SmeltingSlots()
    {
        Slots = [new SmeltingSlot(), new SmeltingSlot(), new SmeltingSlot()];
    }
}
