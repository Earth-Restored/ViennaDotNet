namespace ViennaDotNet.DB.Models.Player.Workshop;

public sealed class CraftingSlots
{
    public CraftingSlot[] Slots { get; init; }

    public CraftingSlots()
    {
        Slots = [new CraftingSlot(), new CraftingSlot(), new CraftingSlot()];
    }
}
