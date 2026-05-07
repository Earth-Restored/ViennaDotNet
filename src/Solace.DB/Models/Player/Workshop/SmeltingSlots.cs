namespace Solace.DB.Models.Player.Workshop;

public sealed class SmeltingSlots : IEquatable<SmeltingSlots>
{
    public SmeltingSlot[] Slots { get; init; }

    public SmeltingSlots()
    {
        Slots = [new SmeltingSlot(), new SmeltingSlot(), new SmeltingSlot()];
    }

    public bool Equals(SmeltingSlots? other)
        => other is not null && Slots.SequenceEqual(other.Slots);

    public override bool Equals(object? obj)
        => Equals(obj as SmeltingSlots);

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

public sealed class SmeltingSlotsEF : IVersionedEntity
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Account Account { get; set; } = null!;

    public SmeltingSlot[] Slots { get; set; } =  [new SmeltingSlot(), new SmeltingSlot(), new SmeltingSlot()];
}
