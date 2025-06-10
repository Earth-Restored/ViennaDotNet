using Newtonsoft.Json;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Player;

[JsonObject(MemberSerialization.OptIn)]
public sealed class Hotbar
{
    [JsonProperty]
    public Item?[] items;

    public Hotbar()
    {
        items = new Item[7];
    }

    public void limitToInventory(Inventory inventory)
    {
        Dictionary<string, int?> usedStackableItemCounts = [];
        Dictionary<string, HashSet<string>> usedNonStackableItemInstances = [];

        for (int index = 0; index < items.Length; index++)
        {
            Item? item = items[index];
            if (item is null)
            {
                continue;
            }

            if (item.instanceId is not null)
            {
                if (inventory.getItemInstance(item.uuid, item.instanceId) is not null)
                {
                    var usedItemInstances = usedNonStackableItemInstances.ComputeIfAbsent(item.uuid, uuid => [])!;

                    if (!usedItemInstances.Add(item.instanceId))
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
                int inventoryCount = inventory.getItemCount(item.uuid);

                int usedCount = usedStackableItemCounts.GetValueOrDefault(item.uuid) ?? 0;
                if (inventoryCount - usedCount > 0)
                {
                    if (inventoryCount - usedCount < item.count)
                    {
                        item = new Item(item.uuid, inventoryCount - usedCount, null);
                    }

                    usedCount += item.count;
                    usedStackableItemCounts[item.uuid] = usedCount;
                }
                else
                {
                    item = null;
                }
            }

            items[index] = item;
        }
    }

    public sealed record Item(
        string uuid,
        int count,
        string? instanceId
    );
}
