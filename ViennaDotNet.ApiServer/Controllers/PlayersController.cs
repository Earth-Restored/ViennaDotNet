using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Serilog;
using Uma.Uuid;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Catalog;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Common;
using ViennaDotNet.DB.Models.Player;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Route("buildplate")]
    public class PlayersController : ControllerBase
    {
        private static EarthDB earthDB => Program.DB;
        private static Catalog catalog => Program.Catalog;

        record JoinRequest(
            string uuid,
            string joinCode
        )
        {
        }
        record JoinResponse(
            bool accepted,
            JoinResponse.Inventory inventory
        )
        {
            public record Inventory(
                Inventory.Item[] items,
                Inventory.HotbarItem?[] hotbar
            )
            {
                public record Item(
                    string id,
                    int count,
                    string? instanceId,
                    int wear
                )
                {
                }

                public record HotbarItem(
                    string id,
                    int count,
                    string? instanceId

                )
                {
                }
            }
        }
        [HttpPost]
        [Route("join/{instanceId}")]
        public async Task<IActionResult> JoinInstance(string instanceId)
        {
            if (!auth())
                return Unauthorized();

            JoinRequest? joinRequest = await Request.Body.AsJson<JoinRequest>();
            if (joinRequest is null)
                return BadRequest();

            string playerId = joinRequest.uuid;

            // TODO: check join code etc.

            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                    .Get("inventory", playerId, typeof(Inventory))
                    .Get("hotbar", playerId, typeof(Hotbar))
                    .Execute(earthDB);
                Inventory inventory = (Inventory)results.Get("inventory").Value;
                Hotbar hotbar = (Hotbar)results.Get("hotbar").Value;

                JoinResponse joinResponse = new JoinResponse(
                    true,
                    new JoinResponse.Inventory(
                        inventory.getStackableItems()
                            .Select(item => new JoinResponse.Inventory.Item(item.id, item.count ?? 0, null, 0))
                            .Concat(
                                inventory.getNonStackableItems()
                                    .SelectMany(item => item.instances
                                    .Select(instance => new JoinResponse.Inventory.Item(item.id, 1, instance.instanceId, instance.wear))
                            /*.forEach(consumer)*/)
                        ).Where(item => item.count > 0).ToArray(),
                        hotbar.items.Select(item => item != null && item.count > 0 ? new JoinResponse.Inventory.HotbarItem(item.uuid, item.count, item.instanceId) : null).ToArray()
                    )
                );

                string resp = JsonConvert.SerializeObject(joinResponse);
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        record LeaveResponse(
        // TODO
        )
        {
        }
        [HttpPost]
        [Route("leave/{instanceId}/{playerId}")]
        public IActionResult LeaveInstance(string instanceId, string playerId)
        {
            if (!auth())
                return Unauthorized();

            // TODO

            string resp = JsonConvert.SerializeObject(new LeaveResponse());
            return Content(resp, "application/json");
        }

        record AddItemRequest(
            string itemId,
            int count,
            string? instanceId,
            int wear
        )
        {
        }
        [HttpPost]
        [Route("inventory/{instanceId}/{playerId}/add")]
        public async Task<IActionResult> Inventory_AddItem(string instanceId, string playerId)
        {
            if (!auth())
                return Unauthorized();

            AddItemRequest? addItemRequest = await Request.Body.AsJson<AddItemRequest>();
            if (addItemRequest is null)
                return BadRequest();

            ItemsCatalog.Item? catalogItem = catalog.itemsCatalog.items.Where(item => item.id == addItemRequest.itemId).FirstOrDefault();
            if (catalogItem == null)
                return BadRequest();

            if (!catalogItem.stacks && addItemRequest.instanceId == null)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("inventory", playerId, typeof(Inventory))
                    .Get("journal", playerId, typeof(Journal))
                    .Then(results1 =>
                    {
                        Inventory inventory = (Inventory)results1.Get("inventory").Value;
                        Journal journal = (Journal)results1.Get("journal").Value;

                        if (catalogItem.stacks)
                            inventory.addItems(addItemRequest.itemId, addItemRequest.count);
                        else
                            inventory.addItems(addItemRequest.itemId, new NonStackableItemInstance[] { new NonStackableItemInstance(addItemRequest.instanceId!, addItemRequest.wear) });

                        journal.touchItem(addItemRequest.itemId, requestStartedOn);

                        return new EarthDB.Query(true)
                            .Update("inventory", playerId, inventory)
                            .Update("journal", playerId, journal);
                    })
                    .Execute(earthDB);

                return Ok();
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        record RemoveItemRequest(
            string itemId,
            int count,
            string? instanceId
        )
        {
        }
        [HttpPost]
        [Route("inventory/{instanceId}/{playerId}/remove")]
        public async Task<IActionResult> Inventory_RemoveItem(string instanceId, string playerId)
        {
            if (!auth())
                return Unauthorized();

            RemoveItemRequest? removeItemRequest = await Request.Body.AsJson<RemoveItemRequest>();
            if (removeItemRequest is null)
                return BadRequest();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("inventory", playerId, typeof(Inventory))
                    .Get("hotbar", playerId, typeof(Hotbar))
                    .Then(results1 =>
                    {
                        Inventory inventory = (Inventory)results1.Get("inventory").Value;
                        Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                        if (removeItemRequest.instanceId != null)
                        {
                            if (inventory.takeItems(removeItemRequest.itemId, new string[] { removeItemRequest.instanceId }) == null)
                                Log.Warning($"Buildplate instance {instanceId} attempted to remove item {removeItemRequest.itemId} {removeItemRequest.instanceId} from player {playerId} that is not in inventory");
                        }
                        else
                        {
                            if (!inventory.takeItems(removeItemRequest.itemId, removeItemRequest.count))
                            {
                                int count = inventory.getItemCount(removeItemRequest.itemId);
                                if (!inventory.takeItems(removeItemRequest.itemId, count))
                                    count = 0;

                                Log.Warning($"Buildplate instance {instanceId} attempted to remove item {removeItemRequest.itemId} {removeItemRequest.count - count} from player {playerId} that is not in inventory");
                            }
                        }

                        hotbar.limitToInventory(inventory);

                        return new EarthDB.Query(true)
                            .Update("inventory", playerId, inventory)
                            .Update("hotbar", playerId, hotbar);
                    })
                    .Execute(earthDB);

                return Ok();
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        record UpdateWearRequest(
            string itemId,
            string instanceId,
            int wear
        )
        {
        }
        [HttpPost]
        [Route("inventory/{instanceId}/{playerId}/updateWear")]
        public async Task<IActionResult> Inventory_UpdateWear(string instanceId, string playerId)
        {
            if (!auth())
                return Unauthorized();

            UpdateWearRequest? updateWearRequest = await Request.Body.AsJson<UpdateWearRequest>();
            if (updateWearRequest is null)
                return BadRequest();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("inventory", playerId, typeof(Inventory))
                    .Then(results1 =>
                    {
                        Inventory inventory = (Inventory)results1.Get("inventory").Value;

                        NonStackableItemInstance? nonStackableItemInstance = inventory.getItemInstance(updateWearRequest.itemId, updateWearRequest.instanceId);
                        if (nonStackableItemInstance != null)
                        {
                            // TODO: make NonStackableItemInstance mutable instead of doing this
                            if (inventory.takeItems(updateWearRequest.itemId, new string[] { updateWearRequest.instanceId }) == null)
                                throw new InvalidOperationException();

                            inventory.addItems(updateWearRequest.itemId, new NonStackableItemInstance[] { new NonStackableItemInstance(updateWearRequest.instanceId, updateWearRequest.wear) });
                        }
                        else
                        {
                            Log.Warning($"Buildplate instance {instanceId} attempted to update item wear for item {updateWearRequest.itemId} {updateWearRequest.instanceId} player {playerId} that is not in inventory");
                        }

                        return new EarthDB.Query(true)
                            .Update("inventory", playerId, inventory);
                    })
                    .Execute(earthDB);

                return Ok();
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        record SetHotbarRequest(
            SetHotbarRequest.Item[] items
        )
        {
            public record Item(
                string itemId,
                int count,
                string? instanceId
            )
            {
            }
        }
        [HttpPost]
        [Route("inventory/{instanceId}/{playerId}/hotbar")]
        public async Task<IActionResult> Inventory_SetHotbar(string instanceId, string playerId)
        {
            if (!auth())
                return Unauthorized();

            SetHotbarRequest? setHotbarRequest = await Request.Body.AsJson<SetHotbarRequest>();
            if (setHotbarRequest is null)
                return BadRequest();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("inventory", playerId, typeof(Inventory))
                    .Then(results1 =>
                    {
                        Inventory inventory = (Inventory)results1.Get("inventory").Value;

                        Hotbar hotbar = new Hotbar();
                        for (int index = 0; index < hotbar.items.Length; index++)
                        {
                            SetHotbarRequest.Item item = setHotbarRequest.items[index];
                            hotbar.items[index] = item != null ? new Hotbar.Item(item.itemId, item.count, item.instanceId) : null;
                        }

                        hotbar.limitToInventory(inventory);

                        return new EarthDB.Query(true)
                            .Update("hotbar", playerId, hotbar);
                    })
                    .Execute(earthDB);

                return Ok();
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        private bool auth()
        {
            if (!Request.Headers.TryGetValue("Vienna-Buildplate-Instance-Token", out StringValues token))
            {
                Log.Warning("Request didn't have 'Vienna-Buildplate-Instance-Token' header");
                return false;
            }

            // TODO: validate token

            return true;
        }
    }
}
