using BitcoderCZ.Utils;
using Solace.Common.Utils;

namespace Solace.DB.Models.Player;

public sealed class Hotbar : IEquatable<Hotbar>
{
    public Item?[] Items { get; set; }

    public Hotbar()
    {
        Items = new Item[7];
    }

    public void LimitToInventory(Inventory inventory)
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

    public bool Equals(Hotbar? other)
        => other is not null && Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj)
        => Equals(obj as Hotbar);

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
