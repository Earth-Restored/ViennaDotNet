using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.DB;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Inventory;
using ViennaDotNet.ApiServer.Utils;
using System.Linq;
using Newtonsoft.Json;
using static ViennaDotNet.ApiServer.Types.Inventory.NonStackableInventoryItem;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}/inventory/survival")]
    public class InventoryController : ControllerBase
    {
        private static EarthDB earthDB => Program.DB;
        private static Catalog catalog => Program.Catalog;

        public IActionResult GetInventory()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            Inventory inventoryModel;
            Hotbar hotbarModel;
            Journal journalModel;
            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                        .Get("inventory", playerId, typeof(Inventory))
                        .Get("hotbar", playerId, typeof(Hotbar))
                        .Get("journal", playerId, typeof(Journal))
                        .Execute(earthDB);
                inventoryModel = (Inventory)results.Get("inventory").Value;
                hotbarModel = (Hotbar)results.Get("hotbar").Value;
                journalModel = (Journal)results.Get("journal").Value;
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }

            Dictionary<string, object> inventory = new();

            HotbarItem?[] hotbarItems = hotbarModel.items.Select(item => item != null ? new HotbarItem(
                    item.uuid,
                    item.count,
                    item.instanceId,
                    item.instanceId != null ? ItemWear.wearToHealth(item.uuid, inventoryModel.getItemInstance(item.uuid, item.instanceId)!.wear, catalog.itemsCatalog) : 0.0f
            ) : null).ToArray();
            inventory["hotbar"] = hotbarItems;
            Dictionary<string, int?> hotbarItemCounts = new();
            Array.ForEach(hotbarModel.items, item =>
            {
                if (item != null)
                    hotbarItemCounts[item.uuid] = hotbarItemCounts.GetOrDefault(item.uuid, 0) + item.count;
            });
            StackableInventoryItem[] stackableItems = inventoryModel.getStackableItems().Select(item =>
            {
                string uuid = item.id;
                int count = item.count!.Value - hotbarItemCounts.GetOrDefault(uuid, 0)!.Value;
                Journal.ItemJournalEntry itemJournalEntry = journalModel.getItem(uuid)!;
                string firstSeen = TimeFormatter.FormatTime(itemJournalEntry.firstSeen);
                string lastSeen = TimeFormatter.FormatTime(itemJournalEntry.lastSeen);
                return new StackableInventoryItem(
                    uuid,
                    count,
                    1,
                    new StackableInventoryItem.On(firstSeen),
                    new StackableInventoryItem.On(lastSeen)
                );
            }).ToArray();
            inventory["stackableItems"] = stackableItems;
            HashSet<string> hotbarItemInstances = new();
            Array.ForEach(hotbarModel.items, item =>
            {
                if (item != null && item.instanceId != null)
                    hotbarItemInstances.Add(item.instanceId);
            });
            NonStackableInventoryItem[] nonStackableItems = inventoryModel.getNonStackableItems().Select(item =>
            {
                string uuid = item.id;
                Journal.ItemJournalEntry itemJournalEntry = journalModel.getItem(uuid)!;
                string firstSeen = TimeFormatter.FormatTime(itemJournalEntry.firstSeen);
                string lastSeen = TimeFormatter.FormatTime(itemJournalEntry.lastSeen);
                return new NonStackableInventoryItem(
                    uuid,
                    item.instances.Where(instance => !hotbarItemInstances.Contains(instance.instanceId)).Select(instance => new NonStackableInventoryItem.Instance(instance.instanceId, ItemWear.wearToHealth(item.id, instance.wear, catalog.itemsCatalog))).ToArray(),
                    1,
                    new NonStackableInventoryItem.On(firstSeen),
                    new NonStackableInventoryItem.On(lastSeen)
                );
            }).ToArray();
            inventory["nonStackableItems"] = nonStackableItems;

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(inventory));
            return Content(resp, "application/json");
        }

        [Route("hotbar")]
        public async Task<IActionResult> GetHotbar()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            SetHotbarRequestItem[]? setHotbarRequestItems = await Request.Body.AsJson<SetHotbarRequestItem[]>();
            if (setHotbarRequestItems is null || setHotbarRequestItems.Length != 7)
                return BadRequest();

            Inventory inventoryModel;
            Hotbar hotbarModel;
            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("inventory", playerId, typeof(Inventory))
                    .Then(results1 =>
                    {
                        Hotbar hotbar = new Hotbar();
                        for (int index = 0; index < hotbar.items.Length; index++)
                        {
                            SetHotbarRequestItem item = setHotbarRequestItems[index];
                            hotbar.items[index] = item != null ? new Hotbar.Item(item.id, item.count, item.instanceId) : null;
                        }
                        hotbar.limitToInventory((Inventory)results1.Get("inventory").Value);
                        return new EarthDB.Query(true)
                            .Update("hotbar", playerId, hotbar)
                            .Get("inventory", playerId, typeof(Inventory))
                            .Get("hotbar", playerId, typeof(Hotbar));
                    })
                    .Execute(earthDB);

                inventoryModel = (Inventory)results.Get("inventory").Value;
                hotbarModel = (Hotbar)results.Get("hotbar").Value;
            }
            catch (EarthDB.DatabaseException ex)
            {
                throw new ServerErrorException(ex);
            }

            HotbarItem?[] hotbarItems = hotbarModel.items.Select(item => item != null ? new HotbarItem(
                item.uuid,
                item.count,
                item.instanceId,
                item.instanceId != null ? ItemWear.wearToHealth(item.uuid, inventoryModel.getItemInstance(item.uuid, item.instanceId)!.wear, catalog.itemsCatalog) : 0.0f
            ) : null).ToArray();

            string resp = JsonConvert.SerializeObject(hotbarItems);
            return Content(resp, "application/json");
        }

        record SetHotbarRequestItem(
            string id,
            int count,
            string? instanceId
        )
        {
        }
    }
}
