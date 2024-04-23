using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Uma.Uuid;
using ViennaDotNet.ApiServer.Exceptions;

using Catalog = ViennaDotNet.ApiServer.Catalog;
using ItemsCatalog = ViennaDotNet.ApiServer.Types.Catalog.ItemsCatalog;
using RecipesCatalog = ViennaDotNet.ApiServer.Types.Catalog.RecipesCatalog;
using BurnRate = ViennaDotNet.ApiServer.Types.Common.BurnRate;
using ExpectedPurchasePrice = ViennaDotNet.ApiServer.Types.Common.ExpectedPurchasePrice;
using SplitRubies = ViennaDotNet.ApiServer.Types.Profile.SplitRubies;
using FinishPrice = ViennaDotNet.ApiServer.Types.Workshop.FinishPrice;
using OutputItem = ViennaDotNet.ApiServer.Types.Workshop.OutputItem;
using State = ViennaDotNet.ApiServer.Types.Workshop.State;
using UnlockPrice = ViennaDotNet.ApiServer.Types.Workshop.UnlockPrice;
using CraftingCalculator = ViennaDotNet.ApiServer.Utils.CraftingCalculator;
using EarthApiResponse = ViennaDotNet.ApiServer.Utils.EarthApiResponse;
using Rewards = ViennaDotNet.ApiServer.Utils.Rewards;
using SmeltingCalculator = ViennaDotNet.ApiServer.Utils.SmeltingCalculator;
using TimeFormatter = ViennaDotNet.ApiServer.Utils.TimeFormatter;
using EarthDB = ViennaDotNet.DB.EarthDB;
using NonStackableItemInstance = ViennaDotNet.DB.Models.Common.NonStackableItemInstance;
using Hotbar = ViennaDotNet.DB.Models.Player.Hotbar;
using Inventory = ViennaDotNet.DB.Models.Player.Inventory;
using Journal = ViennaDotNet.DB.Models.Player.Journal;
using Profile = ViennaDotNet.DB.Models.Player.Profile;
using CraftingSlot = ViennaDotNet.DB.Models.Player.Workshop.CraftingSlot;
using CraftingSlots = ViennaDotNet.DB.Models.Player.Workshop.CraftingSlots;
using InputItem = ViennaDotNet.DB.Models.Player.Workshop.InputItem;
using SmeltingSlot = ViennaDotNet.DB.Models.Player.Workshop.SmeltingSlot;
using SmeltingSlots = ViennaDotNet.DB.Models.Player.Workshop.SmeltingSlots;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.Common.Excceptions;
using Microsoft.Extensions.Primitives;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.DB;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}")]
    public class WorkshopRouter : ControllerBase
    {
        private static EarthDB earthDB => Program.DB;
        private static Catalog catalog => Program.Catalog;

        [HttpGet]
        [Route("player/utilityBlocks")]
        public IActionResult GetUtilityBlocks()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            EarthDB.Results.GenericResult<CraftingSlots> craftingSlotsResult;
            EarthDB.Results.GenericResult<SmeltingSlots> smeltingSlotsResult;
            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                    .Get("crafting", playerId, typeof(CraftingSlots))
                    .Get("smelting", playerId, typeof(SmeltingSlots))
                    .Execute(earthDB);
                craftingSlotsResult = results.GetGeneric<CraftingSlots>("crafting");
                smeltingSlotsResult = results.GetGeneric<SmeltingSlots>("smelting");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }

            Dictionary<string, object> workshop = new();
            workshop.Add("crafting", new Dictionary<string, object>()
            {
                { "1", craftingSlotModelToResponseIncludingLocked(craftingSlotsResult.GValue.slots[0], requestStartedOn, craftingSlotsResult.version, 1) },
                {"2", craftingSlotModelToResponseIncludingLocked(craftingSlotsResult.GValue.slots[1], requestStartedOn, craftingSlotsResult.version, 2) },
                {"3", craftingSlotModelToResponseIncludingLocked(craftingSlotsResult.GValue.slots[2], requestStartedOn, craftingSlotsResult.version, 3)},
            });
            workshop.Add("smelting", new Dictionary<string, object>()
            {
                {"1", smeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.GValue.slots[0], requestStartedOn, smeltingSlotsResult.version, 1) },
                {"2", smeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.GValue.slots[1], requestStartedOn, smeltingSlotsResult.version, 2)},
                {"3", smeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.GValue.slots[2], requestStartedOn, smeltingSlotsResult.version, 3)},
            });

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(workshop));
            return Content(resp, "application/json");
        }

        [HttpGet]
        [Route("crafting/{slotIndex}")]
        public IActionResult GetCraftingStatus(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                    .Get("crafting", playerId, typeof(CraftingSlots))
                    .Execute(earthDB);
                EarthDB.Results.GenericResult<CraftingSlots> craftingSlotsResult = results.GetGeneric<CraftingSlots>("crafting");

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(craftingSlotModelToResponseIncludingLocked(craftingSlotsResult.GValue.slots[slotIndex - 1], requestStartedOn, craftingSlotsResult.version, slotIndex)));
                Console.WriteLine($"Status response: {resp}");
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }
        [HttpGet]
        [Route("smelting/{slotIndex}")]
        public IActionResult GetSmeltingStatus(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                    .Get("smelting", playerId, typeof(SmeltingSlots))
                    .Execute(earthDB);
                EarthDB.Results.GenericResult<SmeltingSlots> smeltingSlotsResult = results.GetGeneric<SmeltingSlots>("smelting");

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(this.smeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.GValue.slots[slotIndex - 1], requestStartedOn, smeltingSlotsResult.version, slotIndex)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        [HttpPost]
        [Route("crafting/{slotIndex}/start")]
        public async Task<IActionResult> StartCrafting(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            StartRequestCrafting? startRequest = await Request.Body.AsJson<StartRequestCrafting>();
            if (startRequest is null || startRequest.multiplier < 1)
                return BadRequest();

            if (startRequest.ingredients.Any(item => item == null || item.quantity < 1 || (item.itemInstanceIds != null && item.itemInstanceIds.Length > 0 && item.itemInstanceIds.Length != item.quantity)))
                return BadRequest();

            RecipesCatalog.CraftingRecipe? recipe = catalog.recipesCatalog.crafting.Where(craftingRecipe => craftingRecipe.id == startRequest.recipeId).FirstOrDefault();

            if (recipe == null)
                return BadRequest();

            if (startRequest.ingredients.Length != recipe.ingredients.Length)
                return BadRequest();

            if (recipe.returnItems.Length > 0)
                throw new UnsupportedOperationException(); // TODO: implement returnItems

            for (int index = 0; index < recipe.ingredients.Length; index++)
            {
                RecipesCatalog.CraftingRecipe.Ingredient ingredient = recipe.ingredients[index];
                StartRequestCrafting.Item item = startRequest.ingredients[index];
                if (!ingredient.items.Any(id => id == item.itemId))
                    return BadRequest();

                if (item.quantity != ingredient.quantity * startRequest.multiplier)
                    return BadRequest();
            }

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("crafting", playerId, typeof(CraftingSlots))
                    .Get("inventory", playerId, typeof(Inventory))
                    .Get("hotbar", playerId, typeof(Hotbar))
                    .Then(results1 =>
                    {
                        EarthDB.Query query = new EarthDB.Query(true);

                        CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                        CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];
                        Inventory inventory = (Inventory)results1.Get("inventory").Value;
                        Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                        if (craftingSlot.locked || craftingSlot.activeJob != null)
                            return query;

                        LinkedList<InputItem> inputItems = new();
                        foreach (StartRequestCrafting.Item item in startRequest.ingredients)
                        {
                            if (item.itemInstanceIds == null || item.itemInstanceIds.Length == 0)
                            {
                                if (!inventory.takeItems(item.itemId, item.quantity))
                                    return query;

                                inputItems.AddLast(new InputItem(item.itemId, item.quantity, Array.Empty<NonStackableItemInstance>()));
                            }
                            else
                            {
                                NonStackableItemInstance[]? instances = inventory.takeItems(item.itemId, item.itemInstanceIds);
                                if (instances == null)
                                    return query;

                                inputItems.AddLast(new InputItem(item.itemId, item.quantity, instances));
                            }
                        }
                        hotbar.limitToInventory(inventory);

                        craftingSlot.activeJob = new CraftingSlot.ActiveJob(startRequest.sessionId, recipe.id, requestStartedOn, inputItems.ToArray(), startRequest.multiplier, 0, false);

                        query.Update("crafting", playerId, craftingSlots).Update("inventory", playerId, inventory).Update("hotbar", playerId, hotbar);

                        return query;
                    })
                    .Execute(earthDB);

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object>(), new EarthApiResponse.Updates(results)));
                Console.WriteLine($"Start response: {resp}");
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }
        [HttpPost]
        [Route("smelting/{slotIndex}/start")]
        public async Task<IActionResult> StartSmelting(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            StartRequestSmelting? startRequest = await Request.Body.AsJson<StartRequestSmelting>();
            if (startRequest is null || startRequest.multiplier < 1)
                return BadRequest();

            if (startRequest.input.quantity < 1 || (startRequest.input.itemInstanceIds != null && startRequest.input.itemInstanceIds.Length > 0 && startRequest.input.itemInstanceIds.Length != startRequest.input.quantity))
                return BadRequest();

            if (startRequest.fuel != null && startRequest.fuel.quantity > 0 && startRequest.fuel.itemInstanceIds != null && startRequest.fuel.itemInstanceIds.Length > 0 && startRequest.fuel.itemInstanceIds.Length != startRequest.fuel.quantity)
                return BadRequest();

            RecipesCatalog.SmeltingRecipe? recipe = catalog.recipesCatalog.smelting.Where(smeltingRecipe => smeltingRecipe.id == startRequest.recipeId).FirstOrDefault();
            if (recipe == null)
                return BadRequest();

            if (recipe.returnItems.Length > 0)
                throw new UnsupportedOperationException(); // TODO: implement returnItems

            if (startRequest.fuel != null && (catalog.itemsCatalog.items.Where(item => item.id == startRequest.fuel.itemId).FirstOrDefault()?.Map(item => item.fuelReturnItems.Length > 0) ?? false))
                throw new UnsupportedOperationException(); // TODO: implement returnItems

            if (startRequest.input.itemId != recipe.inputItemId || startRequest.input.quantity != startRequest.multiplier)
                return BadRequest();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("smelting", playerId, typeof(SmeltingSlots))
                    .Get("inventory", playerId, typeof(Inventory))
                    .Get("hotbar", playerId, typeof(Hotbar))
                    .Then(results1 =>
                    {
                        EarthDB.Query query = new EarthDB.Query(true);

                        SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                        SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];
                        Inventory inventory = (Inventory)results1.Get("inventory").Value;
                        Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                        if (smeltingSlot.locked || smeltingSlot.activeJob != null)
                            return query;

                        InputItem input;
                        if (startRequest.input.itemInstanceIds == null || startRequest.input.itemInstanceIds.Length == 0)
                        {
                            if (!inventory.takeItems(startRequest.input.itemId, startRequest.input.quantity))
                                return query;

                            input = new InputItem(startRequest.input.itemId, startRequest.input.quantity, Array.Empty<NonStackableItemInstance>());
                        }
                        else
                        {
                            NonStackableItemInstance[]? instances = inventory.takeItems(startRequest.input.itemId, startRequest.input.itemInstanceIds);
                            if (instances == null)
                                return query;

                            input = new InputItem(startRequest.input.itemId, startRequest.input.quantity, instances);
                        }

                        SmeltingSlot.Fuel? fuel;
                        int requiredFuelHeat = recipe.heatRequired * startRequest.multiplier - (smeltingSlot.burning != null ? smeltingSlot.burning.remainingHeat : 0);
                        if (startRequest.fuel != null && startRequest.fuel.quantity > 0)
                        {
                            if (requiredFuelHeat <= 0)
                                return query;

                            BurnRate? burnRate = catalog.itemsCatalog.items.Where(item => item.id == startRequest.fuel.itemId).FirstOrDefault()?.Map(item => item.burnRate);

                            if (burnRate == null)
                                return query;

                            int requiredFuelCount = 0;
                            while (requiredFuelHeat > 0)
                            {
                                requiredFuelCount += 1;
                                requiredFuelHeat -= burnRate.heatPerSecond * burnRate.burnTime;
                            }

                            if (startRequest.fuel.quantity < requiredFuelCount)
                                return query;

                            InputItem fuelItem;
                            if (startRequest.fuel.itemInstanceIds == null || startRequest.fuel.itemInstanceIds.Length == 0)
                            {
                                if (!inventory.takeItems(startRequest.fuel.itemId, startRequest.fuel.quantity))
                                    return query;

                                fuelItem = new InputItem(startRequest.fuel.itemId, requiredFuelCount, new NonStackableItemInstance[0]);
                            }
                            else
                            {
                                NonStackableItemInstance[]? instances = inventory.takeItems(startRequest.fuel.itemId, startRequest.fuel.itemInstanceIds);

                                if (instances == null)
                                    return query;

                                fuelItem = new InputItem(startRequest.fuel.itemId, requiredFuelCount, instances);
                            }
                            fuel = new SmeltingSlot.Fuel(fuelItem, burnRate.burnTime, burnRate.heatPerSecond);
                        }
                        else
                        {
                            if (requiredFuelHeat > 0)
                                return query;

                            fuel = null;
                        }

                        hotbar.limitToInventory(inventory);

                        smeltingSlot.activeJob = new SmeltingSlot.ActiveJob(startRequest.sessionId, recipe.id, requestStartedOn, input, fuel, startRequest.multiplier, 0, false);

                        query.Update("smelting", playerId, smeltingSlots).Update("inventory", playerId, inventory).Update("hotbar", playerId, hotbar);

                        return query;
                    })
                    .Execute(earthDB);

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object>(), new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        [HttpPost]
        [Route("crafting/{slotIndex}/collectItems")]
        public IActionResult CollectCraftingItems(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("crafting", playerId, typeof(CraftingSlots))
                    .Then(results1 =>
                    {
                        CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                        CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];

                        Rewards rewards = new Rewards();
                        if (craftingSlot.activeJob != null)
                        {
                            CraftingCalculator.State state = CraftingCalculator.calculateState(requestStartedOn, craftingSlot.activeJob, catalog);

                            int quantity = state.availableRounds * state.output.count;
                            if (quantity > 0)
                                rewards.addItem(state.output.id, quantity);

                            if (state.completed)
                                craftingSlot.activeJob = null;
                            else
                            {
                                CraftingSlot.ActiveJob activeJob = craftingSlot.activeJob;
                                craftingSlot.activeJob = new CraftingSlot.ActiveJob(activeJob.sessionId, activeJob.recipeId, activeJob.startTime, activeJob.input, activeJob.totalRounds, activeJob.collectedRounds + state.availableRounds, activeJob.finishedEarly);
                            }
                        }

                        return new EarthDB.Query(true).Update("crafting", playerId, craftingSlots).Then(rewards.toRedeemQuery(playerId, requestStartedOn, catalog));
                    })
                    .Execute(earthDB);

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object>()
                {
                    { "rewards", ((Rewards) results.getExtra("rewards")).toApiResponse() }
                }, new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }
        [HttpPost]
        [Route("smelting/{slotIndex}/collectItems")]
        public IActionResult CollectSmeltingItems(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("smelting", playerId, typeof(SmeltingSlots))
                    .Then(results1 =>

                    {
                        SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                        SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];

                        Rewards rewards = new Rewards();
                        if (smeltingSlot.activeJob != null)
                        {
                            SmeltingCalculator.State state = SmeltingCalculator.calculateState(requestStartedOn, smeltingSlot.activeJob, smeltingSlot.burning, catalog);

                            int quantity = state.availableRounds * state.output.count;
                            if (quantity > 0)
                                rewards.addItem(state.output.id, quantity);

                            if (state.completed)
                            {
                                smeltingSlot.activeJob = null;
                                if (state.remainingHeat > 0)
                                    smeltingSlot.burning = new SmeltingSlot.Burning(
                                        state.currentBurningFuel,
                                        state.remainingHeat
                                    );
                                else
                                    smeltingSlot.burning = null;
                            }
                            else
                            {
                                SmeltingSlot.ActiveJob activeJob = smeltingSlot.activeJob;
                                smeltingSlot.activeJob = new SmeltingSlot.ActiveJob(activeJob.sessionId, activeJob.recipeId, activeJob.startTime, activeJob.input, activeJob.addedFuel, activeJob.totalRounds, activeJob.collectedRounds + state.availableRounds, activeJob.finishedEarly);
                            }
                        }

                        return new EarthDB.Query(true).Update("smelting", playerId, smeltingSlots).Then(rewards.toRedeemQuery(playerId, requestStartedOn, catalog));
                    })
                    .Execute(earthDB);

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object>()
                {
                    { "rewards", ((Rewards) results.getExtra("rewards")).toApiResponse() }
                }, new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        [HttpPost]
        [Route("crafting/{slotIndex}/stop")]
        public IActionResult StopCraftingJob(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("crafting", playerId, typeof(CraftingSlots))
                    .Get("inventory", playerId, typeof(Inventory))
                    .Get("journal", playerId, typeof(Journal))
                    .Then(results1 =>
                    {
                        EarthDB.Query query = new EarthDB.Query(true);
                        query.Get("crafting", playerId, typeof(CraftingSlots));

                        CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                        CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];
                        Inventory inventory = (Inventory)results1.Get("inventory").Value;
                        Journal journal = (Journal)results1.Get("journal").Value;

                        if (craftingSlot.activeJob == null)
                            return query;

                        CraftingCalculator.State state = CraftingCalculator.calculateState(requestStartedOn, craftingSlot.activeJob, catalog);

                        foreach (InputItem inputItem in state.input)
                        {
                            if (inputItem.instances.Length > 0)
                                inventory.addItems(inputItem.id, inputItem.instances.Select(instance => new NonStackableItemInstance(instance.instanceId, instance.wear)).ToArray());
                            else if (inputItem.count > 0)
                                inventory.addItems(inputItem.id, inputItem.count);

                            journal.touchItem(inputItem.id, requestStartedOn);
                        }

                        int outputQuantity = state.availableRounds * state.output.count;
                        if (outputQuantity > 0)
                        {
                            ItemsCatalog.Item item = catalog.itemsCatalog.items.Where(item1 => item1.id == state.output.id).First();
                            if (item.stacks)
                                inventory.addItems(item.id, outputQuantity);
                            else
                            {
                                inventory.addItems(item.id, Java.IntStream.Range(0, outputQuantity).Select(index => new NonStackableItemInstance(U.RandomUuid().ToString(), 0)).ToArray());
                            }
                            journal.touchItem(state.output.id, requestStartedOn);
                        }

                        craftingSlot.activeJob = null;

                        query.Update("crafting", playerId, craftingSlots).Update("inventory", playerId, inventory).Update("journal", playerId, journal);

                        return query;
                    })
                    .Execute(earthDB);

                EarthDB.Results.GenericResult<CraftingSlots> craftingSlotsResult = results.GetGeneric<CraftingSlots>("crafting");

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(craftingSlotModelToResponse(craftingSlotsResult.GValue.slots[slotIndex - 1], requestStartedOn, craftingSlotsResult.version), new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }
        [HttpPost]
        [Route("smelting/{slotIndex}/stop")]
        public IActionResult StopSmeltingJob(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("smelting", playerId, typeof(SmeltingSlots))
                    .Get("inventory", playerId, typeof(Inventory))
                    .Get("journal", playerId, typeof(Journal))
                    .Then(results1 =>
                    {
                        EarthDB.Query query = new EarthDB.Query(true);
                        query.Get("smelting", playerId, typeof(SmeltingSlots));

                        SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                        SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];
                        Inventory inventory = (Inventory)results1.Get("inventory").Value;
                        Journal journal = (Journal)results1.Get("journal").Value;

                        if (smeltingSlot.activeJob == null)
                            return query;

                        SmeltingCalculator.State state = SmeltingCalculator.calculateState(requestStartedOn, smeltingSlot.activeJob, smeltingSlot.burning, catalog);

                        if (state.input.instances.Length > 0)
                            inventory.addItems(state.input.id, state.input.instances.Select(instance => new NonStackableItemInstance(instance.instanceId, instance.wear)).ToArray());
                        else if (state.input.count > 0)
                            inventory.addItems(state.input.id, state.input.count);

                        journal.touchItem(state.input.id, requestStartedOn);

                        int outputQuantity = state.availableRounds * state.output.count;
                        if (outputQuantity > 0)
                        {
                            ItemsCatalog.Item item = catalog.itemsCatalog.items.Where(item1 => item1.id == state.output.id).First();
                            if (item.stacks)
                                inventory.addItems(item.id, outputQuantity);
                            else
                                inventory.addItems(item.id, Java.IntStream.Range(0, outputQuantity).Select(index => new NonStackableItemInstance(U.RandomUuid().ToString(), 0)).ToArray());

                            journal.touchItem(state.output.id, requestStartedOn);
                        }
                        if (state.remainingAddedFuel != null)
                        {
                            if (state.remainingAddedFuel.item.instances.Length > 0)
                                inventory.addItems(state.remainingAddedFuel.item.id, state.remainingAddedFuel.item.instances.Select(instance => new NonStackableItemInstance(instance.instanceId, instance.wear)).ToArray());
                            else if (state.remainingAddedFuel.item.count > 0)
                                inventory.addItems(state.remainingAddedFuel.item.id, state.remainingAddedFuel.item.count);

                            journal.touchItem(state.remainingAddedFuel.item.id, requestStartedOn);
                        }

                        smeltingSlot.activeJob = null;
                        if (state.remainingHeat > 0)
                            smeltingSlot.burning = new SmeltingSlot.Burning(state.currentBurningFuel, state.remainingHeat);
                        else
                            smeltingSlot.burning = null;

                        query.Update("smelting", playerId, smeltingSlots).Update("inventory", playerId, inventory).Update("journal", playerId, journal);

                        return query;
                    })
                    .Execute(earthDB);

                EarthDB.Results.GenericResult<SmeltingSlots> smeltingSlotsResult = results.GetGeneric<SmeltingSlots>("smelting");

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(smeltingSlotModelToResponse(smeltingSlotsResult.GValue.slots[slotIndex - 1], requestStartedOn, smeltingSlotsResult.version), new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        [HttpPost]
        [Route("crafting/{slotIndex}/finish")]
        public async Task<IActionResult> FinishCrafting(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            ExpectedPurchasePrice? expectedPurchasePrice = await Request.Body.AsJson<ExpectedPurchasePrice>();
            if (expectedPurchasePrice is null || expectedPurchasePrice.expectedPurchasePrice < 0)
                return BadRequest();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("crafting", playerId, typeof(CraftingSlots))
                    .Get("profile", playerId, typeof(Profile))
                    .Then(results1 =>
                    {
                        EarthDB.Query query = new EarthDB.Query(true);
                        query.Get("profile", playerId, typeof(Profile));

                        CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                        CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];
                        Profile profile = (Profile)results1.Get("profile").Value;

                        if (craftingSlot.activeJob == null)
                            return query;

                        CraftingCalculator.State state = CraftingCalculator.calculateState(requestStartedOn, craftingSlot.activeJob, catalog);
                        if (state.completed)
                            return query;

                        int remainingTime = (int)(state.totalCompletionTime - requestStartedOn);
                        if (remainingTime < 0)
                            return query;

                        CraftingCalculator.FinishPrice finishPrice = CraftingCalculator.calculateFinishPrice(remainingTime);

                        if (expectedPurchasePrice.expectedPurchasePrice < finishPrice.price)
                            return query;

                        if (!profile.rubies.spend(finishPrice.price))
                            return query;

                        CraftingSlot.ActiveJob activeJob = craftingSlot.activeJob;
                        craftingSlot.activeJob = new CraftingSlot.ActiveJob(activeJob.sessionId, activeJob.recipeId, activeJob.startTime, activeJob.input, activeJob.totalRounds, activeJob.collectedRounds, true);
                        query.Update("crafting", playerId, craftingSlots).Update("profile", playerId, profile);

                        return query;
                    })
                    .Execute(earthDB);

                Profile profile = (Profile)results.Get("profile").Value;

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new SplitRubies(profile.rubies.purchased, profile.rubies.earned), new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }
        [HttpPost]
        [Route("smelting/{slotIndex}/finish")]
        public async Task<IActionResult> FinishSmelting(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            ExpectedPurchasePrice? expectedPurchasePrice = await Request.Body.AsJson<ExpectedPurchasePrice>();
            if (expectedPurchasePrice is null || expectedPurchasePrice.expectedPurchasePrice < 0)
                return BadRequest();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("smelting", playerId, typeof(SmeltingSlots))
                    .Get("profile", playerId, typeof(Profile))
                    .Then(results1 =>
                    {
                        EarthDB.Query query = new EarthDB.Query(true);
                        query.Get("profile", playerId, typeof(Profile));

                        SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                        SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];
                        Profile profile = (Profile)results1.Get("profile").Value;

                        if (smeltingSlot.activeJob == null)
                            return query;

                        SmeltingCalculator.State state = SmeltingCalculator.calculateState(requestStartedOn, smeltingSlot.activeJob, smeltingSlot.burning, catalog);
                        if (state.completed)
                            return query;

                        int remainingTime = (int)(state.totalCompletionTime - requestStartedOn);
                        if (remainingTime < 0)
                            return query;

                        SmeltingCalculator.FinishPrice finishPrice = SmeltingCalculator.calculateFinishPrice(remainingTime);

                        if (expectedPurchasePrice.expectedPurchasePrice < finishPrice.price)
                            return query;

                        if (!profile.rubies.spend(finishPrice.price))
                            return query;

                        SmeltingSlot.ActiveJob activeJob = smeltingSlot.activeJob;
                        smeltingSlot.activeJob = new SmeltingSlot.ActiveJob(activeJob.sessionId, activeJob.recipeId, activeJob.startTime, activeJob.input, activeJob.addedFuel, activeJob.totalRounds, activeJob.collectedRounds, true);

                        query.Update("smelting", playerId, smeltingSlots).Update("profile", playerId, profile);

                        return query;
                    })
                    .Execute(earthDB);

                Profile profile = (Profile)results.Get("profile").Value;

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new SplitRubies(profile.rubies.purchased, profile.rubies.earned), new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        [HttpGet]
        [Route("crafting/finish/price")]
        public IActionResult GetCraftingPrice()
        {
            //TimeSpan remainingTime = TimeSpan.Parse(Request.Query["remainingTime"]);

            if (!Request.Query.TryGetValue("remainingTime", out StringValues _remainingTime))
                return BadRequest();

            int remainingTime;
            try
            {
                remainingTime = (int)TimeFormatter.ParseDuration(_remainingTime.ToString());
                if (remainingTime < 0)
                    return BadRequest();
            }
            catch
            {
                return BadRequest();
            }

            CraftingCalculator.FinishPrice finishPrice = CraftingCalculator.calculateFinishPrice(remainingTime);

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(new FinishPrice(finishPrice.price, 0, TimeFormatter.FormatDuration(finishPrice.validFor))));
            return Content(resp, "application/json");
        }
        [HttpGet]
        [Route("smelting/finish/price")]
        public IActionResult GetSmeltingPrice()
        {
            //TimeSpan remainingTime = TimeSpan.Parse(Request.Query["remainingTime"]);

            if (!Request.Query.TryGetValue("remainingTime", out StringValues _remainingTime))
                return BadRequest();

            int remainingTime;
            try
            {
                remainingTime = (int)TimeFormatter.ParseDuration(_remainingTime.ToString());
                if (remainingTime < 0)
                    return BadRequest();
            }
            catch
            {
                return BadRequest();
            }

            SmeltingCalculator.FinishPrice finishPrice = SmeltingCalculator.calculateFinishPrice(remainingTime);

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(new FinishPrice(finishPrice.price, 0, TimeFormatter.FormatDuration(finishPrice.validFor))));
            return Content(resp, "application/json");
        }

        [HttpPost]
        [Route("crafting/{slotIndex}/unlock")]
        public async Task<IActionResult> UnlockCraftingSlot(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            ExpectedPurchasePrice? expectedPurchasePrice = await Request.Body.AsJson<ExpectedPurchasePrice>();
            if (expectedPurchasePrice is null || expectedPurchasePrice.expectedPurchasePrice < 0)
                return BadRequest();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("crafting", playerId, typeof(CraftingSlots))
                    .Get("profile", playerId, typeof(Profile))
                    .Then(results1 =>
                    {
                        EarthDB.Query query = new EarthDB.Query(true);

                        CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                        CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];
                        Profile profile = (Profile)results1.Get("profile").Value;

                        if (!craftingSlot.locked)
                            return query;

                        int unlockPrice = CraftingCalculator.calculateUnlockPrice(slotIndex);

                        if (expectedPurchasePrice.expectedPurchasePrice != unlockPrice)
                            return query;

                        if (!profile.rubies.spend(unlockPrice))
                            return query;

                        craftingSlot.locked = false;

                        query.Update("crafting", playerId, craftingSlots).Update("profile", playerId, profile);

                        return query;
                    })
                    .Execute(earthDB);

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object>(), new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }
        [HttpPost]
        [Route("smelting/{slotIndex}/unlock")]
        public async Task<IActionResult> UnlockSmeltingSlot(int slotIndex)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
                return BadRequest();

            ExpectedPurchasePrice? expectedPurchasePrice = await Request.Body.AsJson<ExpectedPurchasePrice>();
            if (expectedPurchasePrice is null || expectedPurchasePrice.expectedPurchasePrice < 0)
                return BadRequest();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("smelting", playerId, typeof(SmeltingSlots))
                    .Get("profile", playerId, typeof(Profile))
                    .Then(results1 =>
                    {
                        EarthDB.Query query = new EarthDB.Query(true);

                        SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                        SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];
                        Profile profile = (Profile)results1.Get("profile").Value;

                        if (!smeltingSlot.locked)
                            return query;

                        int unlockPrice = SmeltingCalculator.calculateUnlockPrice(slotIndex);

                        if (expectedPurchasePrice.expectedPurchasePrice != unlockPrice)
                            return query;

                        if (!profile.rubies.spend(unlockPrice))
                            return query;

                        smeltingSlot.locked = false;

                        query.Update("smelting", playerId, smeltingSlots).Update("profile", playerId, profile);

                        return query;
                    })
                    .Execute(earthDB);

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object>(), new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }
        }

        private Types.Workshop.CraftingSlot craftingSlotModelToResponseIncludingLocked(CraftingSlot craftingSlotModel, long currentTime, int streamVersion, int slotIndex)
        {
            if (craftingSlotModel.locked)
                return new Types.Workshop.CraftingSlot(null, null, null, null, 0, 0, 0, null, null, State.LOCKED, null, new UnlockPrice(CraftingCalculator.calculateUnlockPrice(slotIndex), 0), streamVersion);
            else
                return craftingSlotModelToResponse(craftingSlotModel, currentTime, streamVersion);
        }

        private Types.Workshop.CraftingSlot craftingSlotModelToResponse(CraftingSlot craftingSlotModel, long currentTime, int streamVersion)
        {
            if (craftingSlotModel.locked)
                throw new ArgumentException(nameof(craftingSlotModel));

            CraftingSlot.ActiveJob? activeJob = craftingSlotModel.activeJob;
            if (activeJob != null)
            {
                CraftingCalculator.State state = CraftingCalculator.calculateState(currentTime, activeJob, catalog);
                return new Types.Workshop.CraftingSlot(
                    activeJob.sessionId,
                    activeJob.recipeId,
                    new OutputItem(state.output.id, state.output.count),
                    activeJob.input.Select(item => new Types.Workshop.InputItem(
                        item.id,
                        item.count,
                        item.instances.Select(item => item.instanceId).ToArray()
                    )).ToArray(),
                    state.completedRounds,
                    state.availableRounds,
                    state.totalRounds,
                    !state.completed ? TimeFormatter.FormatTime(state.nextCompletionTime) : null,
                    !state.completed ? TimeFormatter.FormatTime(state.totalCompletionTime) : null,
                    state.completed ? State.COMPLETED : State.ACTIVE,
                    null,
                    null,
                    streamVersion
                );
            }
            else
                return new Types.Workshop.CraftingSlot(null, null, null, null, 0, 0, 0, null, null, State.EMPTY, null, null, streamVersion);
        }

        private Types.Workshop.SmeltingSlot smeltingSlotModelToResponseIncludingLocked(SmeltingSlot smeltingSlotModel, long currentTime, int streamVersion, int slotIndex)
        {
            if (smeltingSlotModel.locked)
                return new Types.Workshop.SmeltingSlot(null, null, null, null, null, null, 0, 0, 0, null, null, State.LOCKED, null, new UnlockPrice(SmeltingCalculator.calculateUnlockPrice(slotIndex), 0), streamVersion);
            else
                return smeltingSlotModelToResponse(smeltingSlotModel, currentTime, streamVersion);
        }

        private Types.Workshop.SmeltingSlot smeltingSlotModelToResponse(SmeltingSlot smeltingSlotModel, long currentTime, int streamVersion)
        {
            if (smeltingSlotModel.locked)
                throw new ArgumentException(nameof(smeltingSlotModel));

            SmeltingSlot.ActiveJob? activeJob = smeltingSlotModel.activeJob;
            if (activeJob != null)
            {
                SmeltingCalculator.State state = SmeltingCalculator.calculateState(currentTime, activeJob, smeltingSlotModel.burning, catalog);

                Types.Workshop.SmeltingSlot.Fuel? fuel;
                if (state.remainingAddedFuel != null && state.remainingAddedFuel.item.count > 0)
                {
                    fuel = new Types.Workshop.SmeltingSlot.Fuel(
                        new BurnRate(state.remainingAddedFuel.burnDuration, state.remainingAddedFuel.heatPerSecond),
                        state.remainingAddedFuel.item.id,
                        state.remainingAddedFuel.item.count,
                        state.remainingAddedFuel.item.instances.Select(item => item.instanceId).ToArray()
                    );
                }
                else
                    fuel = null;

                Types.Workshop.SmeltingSlot.Burning burning = new Types.Workshop.SmeltingSlot.Burning(
                    !state.completed ? TimeFormatter.FormatTime(state.burnStartTime) : null,
                    !state.completed ? TimeFormatter.FormatTime(state.burnEndTime) : null,
                    TimeFormatter.FormatDuration((state.remainingHeat * 1000) / state.currentBurningFuel.heatPerSecond),
                    (float)state.currentBurningFuel.burnDuration * state.currentBurningFuel.heatPerSecond - state.remainingHeat,
                    new Types.Workshop.SmeltingSlot.Fuel(
                        new BurnRate(state.currentBurningFuel.burnDuration, state.currentBurningFuel.heatPerSecond),
                        state.currentBurningFuel.item.id,
                        state.currentBurningFuel.item.count,
                        state.currentBurningFuel.item.instances.Select(item => item.instanceId).ToArray()
                    )
                );

                return new Types.Workshop.SmeltingSlot(
                    fuel,
                    burning,
                    activeJob.sessionId,
                    activeJob.recipeId,
                    new OutputItem(state.output.id, state.output.count),
                    state.input.count > 0 ? new Types.Workshop.InputItem[] { new Types.Workshop.InputItem(state.input.id, state.input.count, state.input.instances.Select(item => item.instanceId).ToArray()) } : new Types.Workshop.InputItem[0],
                    state.completedRounds,
                    state.availableRounds,
                    state.totalRounds,
                    !state.completed ? TimeFormatter.FormatTime(state.nextCompletionTime) : null,
                    !state.completed ? TimeFormatter.FormatTime(state.totalCompletionTime) : null,
                    state.completed ? State.COMPLETED : State.ACTIVE,
                    null,
                    null,
                    streamVersion
                );
            }
            else
            {
                SmeltingSlot.Burning? burningModel = smeltingSlotModel.burning;
                Types.Workshop.SmeltingSlot.Burning? burning = burningModel != null ? new Types.Workshop.SmeltingSlot.Burning(
                    null,
                    null,
                    TimeFormatter.FormatDuration((burningModel.remainingHeat * 1000) / burningModel.fuel.heatPerSecond),
                    (float)burningModel.fuel.burnDuration * burningModel.fuel.heatPerSecond * burningModel.fuel.item.count - burningModel.remainingHeat,
                    new Types.Workshop.SmeltingSlot.Fuel(
                        new BurnRate(burningModel.fuel.burnDuration, burningModel.fuel.heatPerSecond),
                        burningModel.fuel.item.id,
                        burningModel.fuel.item.count,
                        burningModel.fuel.item.instances.Select(item => item.instanceId).ToArray()
                    )
                ) : null;
                return new Types.Workshop.SmeltingSlot(null, burning, null, null, null, null, 0, 0, 0, null, null, State.EMPTY, null, null, streamVersion);
            }
        }


        record StartRequestCrafting(
            string sessionId,
            string recipeId,
            int multiplier,
            StartRequestCrafting.Item[] ingredients
        )
        {
            public record Item(
                string itemId,
                int quantity,
                string[] itemInstanceIds
            )
            {
            }
        }

        record StartRequestSmelting(
            string sessionId,
            string recipeId,
            int multiplier,
            StartRequestSmelting.Item input,
            StartRequestSmelting.Item? fuel

        )
        {
            public record Item(
                string itemId,
                int quantity,
                string[] itemInstanceIds
            )
            {
            }
        }
    }
}
