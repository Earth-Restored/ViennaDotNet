using BitcoderCZ.Utils;
using Solace.Common.Utils;

namespace Solace.DB.Models.Player;

public sealed class HotbarEF : IEntityWithId<Guid>, IVersionedEntity, IMergeable<HotbarEF>
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Account Account { get; set; } = null!;

    public Item?[] Items { get; set; } = new Item[7];

    public void LimitToInventory(InventoryEF inventory)
    {
        ThrowHelper.ThrowIfNull(inventory);

        Dictionary<string, int?> usedStackableItemCounts = [];
        Dictionary<string, HashSet<string>> usedNonStackableItemInstances = [];

        for (int index = 0; index < Items.Length; index++)
        {
            Item? item = Items[index];
            if (item is null)
            {
                continue;
            }

            if (item.InstanceId is not null)
            {
                if (inventory.GetItemInstance(item.Uuid, item.InstanceId) is not null)
                {
                    var usedItemInstances = usedNonStackableItemInstances.ComputeIfAbsent(item.Uuid, uuid => [])!;

                    if (!usedItemInstances.Add(item.InstanceId))
                    {
                        item = null;
                    }
                }
                else
                {
                    item = null;
                }
            }
            else
            {
                int inventoryCount = inventory.GetItemCount(item.Uuid);

                int usedCount = usedStackableItemCounts.GetValueOrDefault(item.Uuid) ?? 0;
                if (inventoryCount - usedCount > 0)
                {
                    if (inventoryCount - usedCount < item.Count)
                    {
                        item = new Item(item.Uuid, inventoryCount - usedCount, null);
                    }

                    usedCount += item.Count;
                    usedStackableItemCounts[item.Uuid] = usedCount;
                }
                else
                {
                    item = null;
                }
            }

            Items[index] = item;
        }
    }

    public async Task MergeWith(HotbarEF other, ValueMerger merger)
    {
        merger.CurrentUserId = Id.ToString();
        merger.CurrentUsername = Account?.Username;

        for (var i = 0; i < Items.Length; i++)
        {
            Items[i] = await merger.AutoMerge(Items[i], other.Items[i], $"Hotbar slot {i + 1}", null);
        }
    }

    public sealed record Item(
        string Uuid,
        int Count,
        string? InstanceId
    );

    public sealed class Legacy : IEquatable<Legacy>
    {
        public Item?[] Items { get; set; }

        public Legacy()
        {
            Items = new Item[7];
        }

        public bool Equals(Legacy? other)
            => other is not null && Items.SequenceEqual(other.Items);

        public override bool Equals(object? obj)
            => Equals(obj as Legacy);

        public override int GetHashCode()
        {
            var hash = new HashCode();

            foreach (var item in Items)
            {
                hash.Add(item);
            }

            return hash.ToHashCode();
        }

        public sealed record Item(
            string Uuid,
            int Count,
            string? InstanceId
        );
    }
}
