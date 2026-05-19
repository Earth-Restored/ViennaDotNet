using Solace.Common.Utils;

namespace Solace.DB.Models.Player.Workshop;

public sealed class SmeltingSlotsEF : IEntityWithId<Guid>, IVersionedEntity, IMergeable<SmeltingSlotsEF>
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Account Account { get; set; } = null!;

    public SmeltingSlot[] Slots { get; set; } = [new SmeltingSlot(), new SmeltingSlot(), new SmeltingSlot()];

    public async Task MergeWith(SmeltingSlotsEF other, ValueMerger merger)
    {
        merger.CurrentUserId = Id.ToString();
        merger.CurrentUsername = Account?.Username;

        for (var i = 0; i < other.Slots.Length; i++)
        {
            var importSlot = other.Slots[i];
            var slot = Slots[i];

            slot.Locked = await merger.AutoMerge(slot.Locked, importSlot.Locked, $"Smelting slot {i + 1} unlocked", null);

            if (slot.ActiveJob is null)
            {
                slot.ActiveJob = importSlot.ActiveJob;
            }
            else if (importSlot.ActiveJob is not null)
            {
                slot.ActiveJob = (await merger.AutoMerge(slot.ActiveJob.RecipeId, importSlot.ActiveJob.RecipeId, $"Smelting slot {i + 1} recipe", null)) == slot.ActiveJob.RecipeId ? slot.ActiveJob : importSlot.ActiveJob;
            }
        }
    }

    public sealed class Legacy : IEquatable<Legacy>
    {
        public SmeltingSlot.Legacy[] Slots { get; init; }

        public Legacy()
        {
            Slots = [new SmeltingSlot.Legacy(), new SmeltingSlot.Legacy(), new SmeltingSlot.Legacy()];
        }

        public bool Equals(Legacy? other)
            => other is not null && Slots.SequenceEqual(other.Slots);

        public override bool Equals(object? obj)
            => Equals(obj as Legacy);

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
}
