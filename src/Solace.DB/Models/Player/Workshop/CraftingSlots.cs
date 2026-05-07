namespace Solace.DB.Models.Player.Workshop;

public sealed class CraftingSlots : IEquatable<CraftingSlots>
{
    public CraftingSlot[] Slots { get; init; }

    public CraftingSlots()
    {
        Slots = [new CraftingSlot(), new CraftingSlot(), new CraftingSlot()];
    }

    public bool Equals(CraftingSlots? other)
        => other is not null && Slots.SequenceEqual(other.Slots);

    public override bool Equals(object? obj)
        => Equals(obj as CraftingSlots);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var item in Slots)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

public sealed class CraftingSlotsEF : IVersionedEntity
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Account Account { get; set; } = null!;

    public CraftingSlot[] Slots { get; set; } = [new CraftingSlot(), new CraftingSlot(), new CraftingSlot()];
}
