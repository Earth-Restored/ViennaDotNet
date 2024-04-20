using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Uma.Uuid;

namespace ViennaDotNet.DB.Models.Player
{
    public sealed class Hotbar
    {
        public Item?[] items;

        public Hotbar()
        {
            items = new Item[7];
        }

        public void limitToInventory(Inventory inventory)
        {
            for (int index = 0; index < items.Length; index++)
            {
                Item? item = items[index];
                if (item is null)
                    continue;

                if (item.instanceId != null)
                {
                    if (inventory.getItemInstance(item.uuid, item.instanceId) != null)
                        continue;
                    else
                        item = null;
                }
                else
                {
                    int inventoryCount = inventory.getItemCount(item.uuid);
                    if (inventoryCount > 0)
                    {
                        if (inventoryCount < item.count)
                            item = new Item(item.uuid, inventoryCount, null);
                        else
                            continue;
                    }
                    else
                        item = null;
                }

                items[index] = item;
            }
        }

        public record Item(
            string uuid,
            int count,
            string? instanceId
        )
        {
        }
    }
}
