using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System.Diagnostics;
using System.Security.Claims;
using Solace.ApiServer.Exceptions;
using Solace.ApiServer.Utils;
using Solace.Common.Excceptions;
using Solace.Common.Utils;
using Solace.DB.Models.Player;
using Solace.StaticData;
using BurnRate = Solace.ApiServer.Types.Common.BurnRate;
using CraftingCalculator = Solace.ApiServer.Utils.CraftingCalculator;
using CraftingSlot = Solace.DB.Models.Player.Workshop.CraftingSlot;
using CraftingSlots = Solace.DB.Models.Player.Workshop.CraftingSlots;
using EarthApiResponse = Solace.ApiServer.Utils.EarthApiResponse;
using EarthDB = Solace.DB.EarthDB;
using ExpectedPurchasePriceR = Solace.ApiServer.Types.Common.ExpectedPurchasePriceR;
using FinishPrice = Solace.ApiServer.Types.Workshop.FinishPrice;
using Hotbar = Solace.DB.Models.Player.Hotbar;
using InputItem = Solace.DB.Models.Player.Workshop.InputItem;
using Inventory = Solace.DB.Models.Player.Inventory;
using Journal = Solace.DB.Models.Player.Journal;
using NonStackableItemInstance = Solace.DB.Models.Common.NonStackableItemInstance;
using OutputItem = Solace.ApiServer.Types.Workshop.OutputItem;
using Profile = Solace.DB.Models.Player.Profile;
using Rewards = Solace.ApiServer.Utils.Rewards;
using SmeltingCalculator = Solace.ApiServer.Utils.SmeltingCalculator;
using SmeltingSlot = Solace.DB.Models.Player.Workshop.SmeltingSlot;
using SmeltingSlots = Solace.DB.Models.Player.Workshop.SmeltingSlots;
using SplitRubies = Solace.ApiServer.Types.Profile.SplitRubies;
using State = Solace.ApiServer.Types.Workshop.State;
using TimeFormatter = Solace.ApiServer.Utils.TimeFormatter;
using UnlockPrice = Solace.ApiServer.Types.Workshop.UnlockPrice;

namespace Solace.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
internal sealed class WorkshopRouter : SolaceControllerBase
{
    private static EarthDB earthDB => Program.DB;
    private static StaticData.StaticData staticData => Program.staticData;

    [HttpGet("player/utilityBlocks")]
    public async Task<Results<ContentHttpResult, BadRequest>> GetUtilityBlocks(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        EarthDB.Results.Result<CraftingSlots> craftingSlotsResult;
        EarthDB.Results.Result<SmeltingSlots> smeltingSlotsResult;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .ExecuteAsync(earthDB, cancellationToken);
            craftingSlotsResult = results.GetResult<CraftingSlots>("crafting");
            smeltingSlotsResult = results.GetResult<SmeltingSlots>("smelting");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        Dictionary<string, object> workshop = new()
        {
            ["crafting"] = new Dictionary<string, object>()
            {
                ["1"] = CraftingSlotModelToResponseIncludingLocked(craftingSlotsResult.Value.Slots[0], requestStartedOn, craftingSlotsResult.Version, 1),
                ["2"] = CraftingSlotModelToResponseIncludingLocked(craftingSlotsResult.Value.Slots[1], requestStartedOn, craftingSlotsResult.Version, 2),
                ["3"] = CraftingSlotModelToResponseIncludingLocked(craftingSlotsResult.Value.Slots[2], requestStartedOn, craftingSlotsResult.Version, 3),
            },
            ["smelting"] = new Dictionary<string, object>()
            {
                ["1"] = SmeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.Value.Slots[0], requestStartedOn, smeltingSlotsResult.Version, 1),
                ["2"] = SmeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.Value.Slots[1], requestStartedOn, smeltingSlotsResult.Version, 2),
                ["3"] = SmeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.Value.Slots[2], requestStartedOn, smeltingSlotsResult.Version, 3),
            },
        };

        return EarthJson(workshop);
    }

    [HttpGet("crafting/{slotIndex}")]
    public async Task<Results<ContentHttpResult, BadRequest>> GetCraftingStatus(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .ExecuteAsync(earthDB, cancellationToken);
            EarthDB.Results.Result<CraftingSlots> craftingSlotsResult = results.GetResult<CraftingSlots>("crafting");

            return EarthJson(CraftingSlotModelToResponseIncludingLocked(craftingSlotsResult.Value.Slots[slotIndex - 1], requestStartedOn, craftingSlotsResult.Version, slotIndex));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpGet("smelting/{slotIndex}")]
    public async Task<Results<ContentHttpResult, BadRequest>> GetSmeltingStatus(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .ExecuteAsync(earthDB, cancellationToken);
            EarthDB.Results.Result<SmeltingSlots> smeltingSlotsResult = results.GetResult<SmeltingSlots>("smelting");

            return EarthJson(SmeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.Value.Slots[slotIndex - 1], requestStartedOn, smeltingSlotsResult.Version, slotIndex));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("crafting/{slotIndex}/start")]
    public async Task<Results<ContentHttpResult, BadRequest>> StartCrafting(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        StartRequestCrafting? startRequest = await Request.Body.AsJsonAsync<StartRequestCrafting>(cancellationToken);
        if (startRequest is null || startRequest.Multiplier < 1)
        {
            return TypedResults.BadRequest();
        }

        if (startRequest.Ingredients.Any(item => item is null || item.Quantity < 1 || item.ItemInstanceIds is not null && item.ItemInstanceIds.Length > 0 && item.ItemInstanceIds.Length != item.Quantity))
        {
            return TypedResults.BadRequest();
        }

        Catalog.RecipesCatalogR.CraftingRecipe? recipe = staticData.Catalog.RecipesCatalog.GetCraftingRecipe(startRequest.RecipeId);

        if (recipe is null)
        {
            return TypedResults.BadRequest();
        }

        if (recipe.ReturnItems.Length > 0)
        {
            throw new UnsupportedOperationException(); // TODO: implement returnItems
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("inventory", playerId, typeof(Inventory))
                .Get("hotbar", playerId, typeof(Hotbar))
                .Then(results1 =>
                {
                    var query = new EarthDB.Query(true);

                    var craftingSlots = results1.Get<CraftingSlots>("crafting");
                    CraftingSlot craftingSlot = craftingSlots.Slots[slotIndex - 1];
                    var inventory = results1.Get<Inventory>("inventory");
                    var hotbar = results1.Get<Hotbar>("hotbar");

                    if (craftingSlot.Locked || craftingSlot.ActiveJob is not null)
                    {
                        return query;
                    }

                    var providedItems = new InputItem[startRequest.Ingredients.Length];
                    for (int index = 0; index < startRequest.Ingredients.Length; index++)
                    {
                        StartRequestCrafting.Item item = startRequest.Ingredients[index];
                        if (item.ItemInstanceIds is null || item.ItemInstanceIds.Length == 0)
                        {
                            if (!inventory.TakeItems(item.ItemId, item.Quantity))
                            {
                                return query;
                            }

                            providedItems[index] = new InputItem(item.ItemId, item.Quantity, []);
                        }
                        else
                        {
                            NonStackableItemInstance[]? instances = inventory.TakeItems(item.ItemId, item.ItemInstanceIds);
                            if (instances is null)
                            {
                                return query;
                            }

                            providedItems[index] = new InputItem(item.ItemId, item.Quantity, instances);
                        }
                    }

                    hotbar.LimitToInventory(inventory);

                    LinkedList<LinkedList<InputItem>> inputItems = [];
                    foreach (Catalog.RecipesCatalogR.CraftingRecipe.Ingredient ingredient in recipe.Ingredients)
                    {
                        LinkedList<InputItem> ingredientItems = [];
                        int requiredCount = ingredient.Count * startRequest.Multiplier;
                        for (int index = 0; index < providedItems.Length; index++)
                        {
                            InputItem providedItem = providedItems[index];
                            if (providedItem.Count == 0)
                            {
                                continue;
                            }

                            if (!ingredient.PossibleItemIds.Any(id => id == providedItem.Id))
                            {
                                continue;
                            }

                            if (requiredCount > providedItem.Count)
                            {
                                requiredCount -= providedItem.Count;
                                ingredientItems.AddLast(providedItem);
                                providedItems[index] = new InputItem(providedItem.Id, 0, []);
                            }
                            else
                            {
                                NonStackableItemInstance[] takenInstances;
                                NonStackableItemInstance[] remainingInstances;
                                if (providedItem.Instances.Length > 0)
                                {
                                    takenInstances = ArrayExtensions.CopyOfRange(providedItem.Instances, 0, requiredCount);
                                    remainingInstances = ArrayExtensions.CopyOfRange(providedItem.Instances, requiredCount, providedItem.Count);
                                }
                                else
                                {
                                    takenInstances = [];
                                    remainingInstances = [];
                                }

                                ingredientItems.AddLast(new InputItem(providedItem.Id, requiredCount, takenInstances));
                                providedItems[index] = new InputItem(providedItem.Id, providedItem.Count - requiredCount, remainingInstances);
                                requiredCount = 0;
                            }

                            if (requiredCount == 0)
                            {
                                break;
                            }
                        }

                        if (requiredCount > 0)
                        {
                            return query;
                        }

                        if (ingredientItems.Count == 0)
                        {
                            throw new UnreachableException();
                        }

                        inputItems.AddLast(ingredientItems);
                    }

                    if (inputItems.Count != recipe.Ingredients.Length)
                    {
                        throw new UnreachableException();
                    }

                    if (providedItems.Any(item => item.Count > 0))
                    {
                        return query;
                    }

                    craftingSlot.ActiveJob = new CraftingSlot.ActiveJobR(startRequest.SessionId, recipe.Id, requestStartedOn, [.. inputItems.Select(inputItems1 => inputItems1.ToArray())], startRequest.Multiplier, 0, false);

                    query.Update("crafting", playerId, craftingSlots).Update("inventory", playerId, inventory).Update("hotbar", playerId, hotbar);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            return EarthJson(new Dictionary<string, object>(), new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/start")]
    public async Task<Results<ContentHttpResult, BadRequest>> StartSmelting(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        StartRequestSmelting? startRequest = await Request.Body.AsJsonAsync<StartRequestSmelting>(cancellationToken);
        if (startRequest is null || startRequest.Multiplier < 1)
        {
            return TypedResults.BadRequest();
        }

        if (startRequest.Input.Quantity < 1 || startRequest.Input.ItemInstanceIds is not null && startRequest.Input.ItemInstanceIds.Length > 0 && startRequest.Input.ItemInstanceIds.Length != startRequest.Input.Quantity)
        {
            return TypedResults.BadRequest();
        }

        if (startRequest.Fuel is not null && startRequest.Fuel.Quantity > 0 && startRequest.Fuel.ItemInstanceIds is not null && startRequest.Fuel.ItemInstanceIds.Length > 0 && startRequest.Fuel.ItemInstanceIds.Length != startRequest.Fuel.Quantity)
        {
            return TypedResults.BadRequest();
        }

        Catalog.RecipesCatalogR.SmeltingRecipe? recipe = staticData.Catalog.RecipesCatalog.GetSmeltingRecipe(startRequest.RecipeId);
        Catalog.ItemsCatalogR.Item? fuelCatalogItem = startRequest.Fuel is not null ? staticData.Catalog.ItemsCatalog.GetItem(startRequest.Fuel.ItemId) : null;
        if (recipe is null)
        {
            return TypedResults.BadRequest();
        }

        if (startRequest.Fuel is not null && (fuelCatalogItem is null || fuelCatalogItem.FuelInfo is null))
        {
            return TypedResults.BadRequest();
        }

        if (recipe.ReturnItemId is not null)
        {
            throw new UnsupportedOperationException(); // TODO: implement returnItems
        }

        Debug.Assert(fuelCatalogItem is not null);
        Debug.Assert(fuelCatalogItem.FuelInfo is not null);

        if (startRequest.Fuel is not null && fuelCatalogItem.FuelInfo.ReturnItemId is not null)
        {
            throw new UnsupportedOperationException(); // TODO: implement returnItems
        }

        if (startRequest.Input.ItemId != recipe.Input || startRequest.Input.Quantity != startRequest.Multiplier)
        {
            return TypedResults.BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Get("inventory", playerId, typeof(Inventory))
                .Get("hotbar", playerId, typeof(Hotbar))
                .Then(results1 =>
                {
                    var query = new EarthDB.Query(true);

                    var smeltingSlots = results1.Get<SmeltingSlots>("smelting");
                    SmeltingSlot smeltingSlot = smeltingSlots.Slots[slotIndex - 1];
                    var inventory = results1.Get<Inventory>("inventory");
                    var hotbar = results1.Get<Hotbar>("hotbar");

                    if (smeltingSlot.Locked || smeltingSlot.ActiveJob is not null)
                    {
                        return query;
                    }

                    InputItem input;
                    if (startRequest.Input.ItemInstanceIds is null || startRequest.Input.ItemInstanceIds.Length == 0)
                    {
                        if (!inventory.TakeItems(startRequest.Input.ItemId, startRequest.Input.Quantity))
                        {
                            return query;
                        }

                        input = new InputItem(startRequest.Input.ItemId, startRequest.Input.Quantity, []);
                    }
                    else
                    {
                        NonStackableItemInstance[]? instances = inventory.TakeItems(startRequest.Input.ItemId, startRequest.Input.ItemInstanceIds);
                        if (instances is null)
                        {
                            return query;
                        }

                        input = new InputItem(startRequest.Input.ItemId, startRequest.Input.Quantity, instances);
                    }

                    SmeltingSlot.Fuel? fuel;
                    int requiredFuelHeat = recipe.HeatRequired * startRequest.Multiplier - (smeltingSlot.Burning is not null ? smeltingSlot.Burning.RemainingHeat : 0);
                    if (startRequest.Fuel is not null && startRequest.Fuel.Quantity > 0)
                    {
                        int requiredFuelCount = 0;
                        while (requiredFuelHeat > 0)
                        {
                            requiredFuelCount += 1;
                            requiredFuelHeat -= fuelCatalogItem.FuelInfo.HeatPerSecond * fuelCatalogItem.FuelInfo.BurnTime;
                        }

                        if (startRequest.Fuel.Quantity < requiredFuelCount)
                        {
                            return query;
                        }

                        if (requiredFuelCount > 0)
                        {
                            InputItem fuelItem;
                            if (startRequest.Fuel.ItemInstanceIds is null || startRequest.Fuel.ItemInstanceIds.Length == 0)
                            {
                                if (!inventory.TakeItems(startRequest.Fuel.ItemId, requiredFuelCount))
                                {
                                    return query;
                                }

                                fuelItem = new InputItem(startRequest.Fuel.ItemId, requiredFuelCount, []);
                            }
                            else
                            {
                                NonStackableItemInstance[]? instances = inventory.TakeItems(startRequest.Fuel.ItemId, ArrayExtensions.CopyOfRange(startRequest.Fuel.ItemInstanceIds, 0, requiredFuelCount));
                                if (instances is null)
                                {
                                    return query;
                                }

                                fuelItem = new InputItem(startRequest.Fuel.ItemId, requiredFuelCount, instances);
                            }

                            fuel = new SmeltingSlot.Fuel(fuelItem, fuelCatalogItem.FuelInfo.BurnTime, fuelCatalogItem.FuelInfo.HeatPerSecond);
                        }
                        else
                        {
                            fuel = null;
                        }
                    }
                    else
                    {
                        if (requiredFuelHeat > 0)
                        {
                            return query;
                        }

                        fuel = null;
                    }

                    hotbar.LimitToInventory(inventory);

                    smeltingSlot.ActiveJob = new SmeltingSlot.ActiveJobR(startRequest.SessionId, recipe.Id, requestStartedOn, input, fuel, startRequest.Multiplier, 0, false);

                    query.Update("smelting", playerId, smeltingSlots).Update("inventory", playerId, inventory).Update("hotbar", playerId, hotbar);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            return EarthJson(new Dictionary<string, object>(), new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("crafting/{slotIndex}/collectItems")]
    public async Task<Results<ContentHttpResult, BadRequest>> CollectCraftingItems(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Then(results1 =>
                {
                    var craftingSlots = results1.Get<CraftingSlots>("crafting");
                    CraftingSlot craftingSlot = craftingSlots.Slots[slotIndex - 1];

                    var rewards = new Rewards();
                    if (craftingSlot.ActiveJob is not null)
                    {
                        CraftingCalculator.State state = CraftingCalculator.CalculateState(requestStartedOn, craftingSlot.ActiveJob, staticData.Catalog);

                        int quantity = state.AvailableRounds * state.Output.Count;
                        if (quantity > 0)
                        {
                            rewards.AddItem(state.Output.Id, quantity);
                        }

                        if (state.Completed)
                        {
                            craftingSlot.ActiveJob = null;
                        }
                        else
                        {
                            CraftingSlot.ActiveJobR activeJob = craftingSlot.ActiveJob;
                            craftingSlot.ActiveJob = new CraftingSlot.ActiveJobR(activeJob.SessionId, activeJob.RecipeId, activeJob.StartTime, activeJob.Input, activeJob.TotalRounds, activeJob.CollectedRounds + state.AvailableRounds, activeJob.FinishedEarly);
                        }
                    }

                    return new EarthDB.Query(true)
                        .Update("crafting", playerId, craftingSlots)
                        .Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.CraftingCompletedEntry(requestStartedOn, rewards.ToDBRewardsModel())))
                        .Then(rewards.ToRedeemQuery(playerId, requestStartedOn, staticData));
                })
                .ExecuteAsync(earthDB, cancellationToken);

            return EarthJson(new Dictionary<string, object>()
            {
                { "rewards", ((Rewards) results.GetExtra("rewards")).ToApiResponse() }
            }, new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/collectItems")]
    public async Task<Results<ContentHttpResult, BadRequest>> CollectSmeltingItems(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Then(results1 =>
                {
                    var smeltingSlots = results1.Get<SmeltingSlots>("smelting");
                    SmeltingSlot smeltingSlot = smeltingSlots.Slots[slotIndex - 1];

                    var rewards = new Rewards();
                    if (smeltingSlot.ActiveJob is not null)
                    {
                        SmeltingCalculator.State state = SmeltingCalculator.CalculateState(requestStartedOn, smeltingSlot.ActiveJob, smeltingSlot.Burning, staticData.Catalog);

                        int quantity = state.AvailableRounds * state.Output.Count;
                        if (quantity > 0)
                        {
                            rewards.AddItem(state.Output.Id, quantity);
                        }

                        if (state.Completed)
                        {
                            smeltingSlot.ActiveJob = null;
                            if (state.RemainingHeat > 0)
                            {
                                smeltingSlot.Burning = new SmeltingSlot.BurningR(
                                    state.CurrentBurningFuel,
                                    state.RemainingHeat
                                );
                            }
                            else
                            {
                                smeltingSlot.Burning = null;
                            }
                        }
                        else
                        {
                            SmeltingSlot.ActiveJobR activeJob = smeltingSlot.ActiveJob;
                            smeltingSlot.ActiveJob = new SmeltingSlot.ActiveJobR(activeJob.SessionId, activeJob.RecipeId, activeJob.StartTime, activeJob.Input, activeJob.AddedFuel, activeJob.TotalRounds, activeJob.CollectedRounds + state.AvailableRounds, activeJob.FinishedEarly);
                        }
                    }

                    return new EarthDB.Query(true)
                        .Update("smelting", playerId, smeltingSlots)
                        .Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.SmeltingCompletedEntry(requestStartedOn, rewards.ToDBRewardsModel())))
                        .Then(rewards.ToRedeemQuery(playerId, requestStartedOn, staticData));
                })
                .ExecuteAsync(earthDB, cancellationToken);

            return EarthJson(new Dictionary<string, object>()
            {
                { "rewards", ((Rewards) results.GetExtra("rewards")).ToApiResponse() }
            }, new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("crafting/{slotIndex}/stop")]
    public async Task<Results<ContentHttpResult, BadRequest>> StopCraftingJob(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("inventory", playerId, typeof(Inventory))
                .Get("journal", playerId, typeof(Journal))
                .Then(results1 =>
                {
                    var query = new EarthDB.Query(true);
                    query.Get("crafting", playerId, typeof(CraftingSlots));

                    var craftingSlots = results1.Get<CraftingSlots>("crafting");
                    CraftingSlot craftingSlot = craftingSlots.Slots[slotIndex - 1];
                    var inventory = results1.Get<Inventory>("inventory");
                    var journal = results1.Get<Journal>("journal");

                    if (craftingSlot.ActiveJob is null)
                    {
                        return query;
                    }

                    CraftingCalculator.State state = CraftingCalculator.CalculateState(requestStartedOn, craftingSlot.ActiveJob, staticData.Catalog);

                    foreach (InputItem inputItem in state.Input)
                    {
                        if (inputItem.Instances.Length > 0)
                        {
                            inventory.AddItems(inputItem.Id, [.. inputItem.Instances.Select(instance => new NonStackableItemInstance(instance.InstanceId, instance.Wear))]);
                        }
                        else if (inputItem.Count > 0)
                        {
                            inventory.AddItems(inputItem.Id, inputItem.Count);
                        }

                        journal.AddCollectedItem(inputItem.Id, requestStartedOn, 0);
                    }

                    var rewards = new Rewards();
                    int outputQuantity = state.AvailableRounds * state.Output.Count;
                    if (outputQuantity > 0)
                    {
                        rewards.AddItem(state.Output.Id, outputQuantity);
                    }

                    craftingSlot.ActiveJob = null;

                    query.Update("crafting", playerId, craftingSlots).Update("inventory", playerId, inventory).Update("journal", playerId, journal);
                    query.Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.CraftingCompletedEntry(requestStartedOn, rewards.ToDBRewardsModel())), false);
                    query.Then(rewards.ToRedeemQuery(playerId, requestStartedOn, staticData), false);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            EarthDB.Results.Result<CraftingSlots> craftingSlotsResult = results.GetResult<CraftingSlots>("crafting");

            return EarthJson(CraftingSlotModelToResponse(craftingSlotsResult.Value.Slots[slotIndex - 1], requestStartedOn, craftingSlotsResult.Version), new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/stop")]
    public async Task<Results<ContentHttpResult, BadRequest>> StopSmeltingJob(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Get("inventory", playerId, typeof(Inventory))
                .Get("journal", playerId, typeof(Journal))
                .Then(results1 =>
                {
                    var query = new EarthDB.Query(true);
                    query.Get("smelting", playerId, typeof(SmeltingSlots));

                    var smeltingSlots = results1.Get<SmeltingSlots>("smelting");
                    SmeltingSlot smeltingSlot = smeltingSlots.Slots[slotIndex - 1];
                    var inventory = results1.Get<Inventory>("inventory");
                    var journal = results1.Get<Journal>("journal");

                    if (smeltingSlot.ActiveJob is null)
                    {
                        return query;
                    }

                    SmeltingCalculator.State state = SmeltingCalculator.CalculateState(requestStartedOn, smeltingSlot.ActiveJob, smeltingSlot.Burning, staticData.Catalog);

                    if (state.Input.Instances.Length > 0)
                    {
                        inventory.AddItems(state.Input.Id, [.. state.Input.Instances.Select(instance => new NonStackableItemInstance(instance.InstanceId, instance.Wear))]);
                    }
                    else if (state.Input.Count > 0)
                    {
                        inventory.AddItems(state.Input.Id, state.Input.Count);
                    }

                    journal.AddCollectedItem(state.Input.Id, requestStartedOn, 0);

                    if (state.RemainingAddedFuel is not null)
                    {
                        if (state.RemainingAddedFuel.Item.Instances.Length > 0)
                        {
                            inventory.AddItems(state.RemainingAddedFuel.Item.Id, [.. state.RemainingAddedFuel.Item.Instances.Select(instance => new NonStackableItemInstance(instance.InstanceId, instance.Wear))]);
                        }
                        else if (state.RemainingAddedFuel.Item.Count > 0)
                        {
                            inventory.AddItems(state.RemainingAddedFuel.Item.Id, state.RemainingAddedFuel.Item.Count);
                        }

                        journal.AddCollectedItem(state.RemainingAddedFuel.Item.Id, requestStartedOn, 0);
                    }

                    var rewards = new Rewards();
                    int outputQuantity = state.AvailableRounds * state.Output.Count;
                    if (outputQuantity > 0)
                    {
                        rewards.AddItem(state.Output.Id, outputQuantity);
                    }

                    smeltingSlot.ActiveJob = null;
                    if (state.RemainingHeat > 0)
                    {
                        smeltingSlot.Burning = new SmeltingSlot.BurningR(state.CurrentBurningFuel, state.RemainingHeat);
                    }
                    else
                    {
                        smeltingSlot.Burning = null;
                    }

                    query.Update("smelting", playerId, smeltingSlots).Update("inventory", playerId, inventory).Update("journal", playerId, journal);
                    query.Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.SmeltingCompletedEntry(requestStartedOn, rewards.ToDBRewardsModel())), false);
                    query.Then(rewards.ToRedeemQuery(playerId, requestStartedOn, staticData), false);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            EarthDB.Results.Result<SmeltingSlots> smeltingSlotsResult = results.GetResult<SmeltingSlots>("smelting");

            return EarthJson(SmeltingSlotModelToResponse(smeltingSlotsResult.Value.Slots[slotIndex - 1], requestStartedOn, smeltingSlotsResult.Version), new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("crafting/{slotIndex}/finish")]
    public async Task<Results<ContentHttpResult, BadRequest>> FinishCrafting(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        ExpectedPurchasePriceR? expectedPurchasePrice = await Request.Body.AsJsonAsync<ExpectedPurchasePriceR>(cancellationToken);
        if (expectedPurchasePrice is null || expectedPurchasePrice.ExpectedPurchasePrice < 0)
        {
            return TypedResults.BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    var query = new EarthDB.Query(true);
                    query.Get("profile", playerId, typeof(Profile));

                    var craftingSlots = results1.Get<CraftingSlots>("crafting");
                    CraftingSlot craftingSlot = craftingSlots.Slots[slotIndex - 1];
                    var profile = results1.Get<Profile>("profile");

                    if (craftingSlot.ActiveJob is null)
                    {
                        return query;
                    }

                    CraftingCalculator.State state = CraftingCalculator.CalculateState(requestStartedOn, craftingSlot.ActiveJob, staticData.Catalog);
                    if (state.Completed)
                    {
                        return query;
                    }

                    int remainingTime = (int)(state.TotalCompletionTime - requestStartedOn);
                    if (remainingTime < 0)
                    {
                        return query;
                    }

                    CraftingCalculator.FinishPrice finishPrice = CraftingCalculator.CalculateFinishPrice(remainingTime);

                    if (expectedPurchasePrice.ExpectedPurchasePrice < finishPrice.Price)
                    {
                        return query;
                    }

                    if (!profile.Rubies.Spend(finishPrice.Price))
                    {
                        return query;
                    }

                    CraftingSlot.ActiveJobR activeJob = craftingSlot.ActiveJob;
                    craftingSlot.ActiveJob = new CraftingSlot.ActiveJobR(activeJob.SessionId, activeJob.RecipeId, activeJob.StartTime, activeJob.Input, activeJob.TotalRounds, activeJob.CollectedRounds, true);
                    query.Update("crafting", playerId, craftingSlots).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            Profile profile = results.Get<Profile>("profile");

            return EarthJson(new SplitRubies(profile.Rubies.Purchased, profile.Rubies.Earned), new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/finish")]
    public async Task<Results<ContentHttpResult, BadRequest>> FinishSmelting(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        ExpectedPurchasePriceR? expectedPurchasePrice = await Request.Body.AsJsonAsync<ExpectedPurchasePriceR>(cancellationToken);
        if (expectedPurchasePrice is null || expectedPurchasePrice.ExpectedPurchasePrice < 0)
        {
            return TypedResults.BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    var query = new EarthDB.Query(true);
                    query.Get("profile", playerId, typeof(Profile));

                    var smeltingSlots = results1.Get<SmeltingSlots>("smelting");
                    SmeltingSlot smeltingSlot = smeltingSlots.Slots[slotIndex - 1];
                    var profile = results1.Get<Profile>("profile");

                    if (smeltingSlot.ActiveJob is null)
                    {
                        return query;
                    }

                    SmeltingCalculator.State state = SmeltingCalculator.CalculateState(requestStartedOn, smeltingSlot.ActiveJob, smeltingSlot.Burning, staticData.Catalog);
                    if (state.Completed)
                    {
                        return query;
                    }

                    int remainingTime = (int)(state.TotalCompletionTime - requestStartedOn);
                    if (remainingTime < 0)
                    {
                        return query;
                    }

                    SmeltingCalculator.FinishPrice finishPrice = SmeltingCalculator.CalculateFinishPrice(remainingTime);

                    if (expectedPurchasePrice.ExpectedPurchasePrice < finishPrice.Price)
                    {
                        return query;
                    }

                    if (!profile.Rubies.Spend(finishPrice.Price))
                    {
                        return query;
                    }

                    SmeltingSlot.ActiveJobR activeJob = smeltingSlot.ActiveJob;
                    smeltingSlot.ActiveJob = new SmeltingSlot.ActiveJobR(activeJob.SessionId, activeJob.RecipeId, activeJob.StartTime, activeJob.Input, activeJob.AddedFuel, activeJob.TotalRounds, activeJob.CollectedRounds, true);

                    query.Update("smelting", playerId, smeltingSlots).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            Profile profile = results.Get<Profile>("profile");

            return EarthJson(new SplitRubies(profile.Rubies.Purchased, profile.Rubies.Earned), new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpGet("crafting/finish/price")]
    public Results<ContentHttpResult, BadRequest> GetCraftingPrice()
    {
        if (!Request.Query.TryGetValue("remainingTime", out StringValues _remainingTime))
        {
            return TypedResults.BadRequest();
        }

        int remainingTime;
        try
        {
            remainingTime = (int)TimeFormatter.ParseDuration(_remainingTime.ToString());
            if (remainingTime < 0)
            {
                return TypedResults.BadRequest();
            }
        }
        catch
        {
            return TypedResults.BadRequest();
        }

        CraftingCalculator.FinishPrice finishPrice = CraftingCalculator.CalculateFinishPrice(remainingTime);

        return EarthJson(new FinishPrice(finishPrice.Price, 0, TimeFormatter.FormatDuration(finishPrice.ValidFor)));
    }

    [HttpGet("smelting/finish/price")]
    public Results<ContentHttpResult, BadRequest> GetSmeltingPrice()
    {
        if (!Request.Query.TryGetValue("remainingTime", out StringValues _remainingTime))
        {
            return TypedResults.BadRequest();
        }

        int remainingTime;
        try
        {
            remainingTime = (int)TimeFormatter.ParseDuration(_remainingTime.ToString());
            if (remainingTime < 0)
            {
                return TypedResults.BadRequest();
            }
        }
        catch
        {
            return TypedResults.BadRequest();
        }

        SmeltingCalculator.FinishPrice finishPrice = SmeltingCalculator.CalculateFinishPrice(remainingTime);

        return EarthJson(new FinishPrice(finishPrice.Price, 0, TimeFormatter.FormatDuration(finishPrice.ValidFor)));
    }

    [HttpPost("crafting/{slotIndex}/unlock")]
    public async Task<Results<ContentHttpResult, BadRequest>> UnlockCraftingSlot(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        ExpectedPurchasePriceR? expectedPurchasePrice = await Request.Body.AsJsonAsync<ExpectedPurchasePriceR>(cancellationToken);
        if (expectedPurchasePrice is null || expectedPurchasePrice.ExpectedPurchasePrice < 0)
        {
            return TypedResults.BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    var query = new EarthDB.Query(true);

                    CraftingSlots craftingSlots = results1.Get<CraftingSlots>("crafting");
                    CraftingSlot craftingSlot = craftingSlots.Slots[slotIndex - 1];
                    Profile profile = results1.Get<Profile>("profile");

                    if (!craftingSlot.Locked)
                    {
                        return query;
                    }

                    int unlockPrice = CraftingCalculator.CalculateUnlockPrice(slotIndex);

                    if (expectedPurchasePrice.ExpectedPurchasePrice != unlockPrice)
                    {
                        return query;
                    }

                    if (!profile.Rubies.Spend(unlockPrice))
                    {
                        return query;
                    }

                    craftingSlot.Locked = false;

                    query.Update("crafting", playerId, craftingSlots).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            return EarthJson(new Dictionary<string, object>(), new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/unlock")]
    public async Task<Results<ContentHttpResult, BadRequest>> UnlockSmeltingSlot(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return TypedResults.BadRequest();
        }

        ExpectedPurchasePriceR? expectedPurchasePrice = await Request.Body.AsJsonAsync<ExpectedPurchasePriceR>(cancellationToken);
        if (expectedPurchasePrice is null || expectedPurchasePrice.ExpectedPurchasePrice < 0)
        {
            return TypedResults.BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    var query = new EarthDB.Query(true);

                    SmeltingSlots smeltingSlots = results1.Get<SmeltingSlots>("smelting");
                    SmeltingSlot smeltingSlot = smeltingSlots.Slots[slotIndex - 1];
                    Profile profile = results1.Get<Profile>("profile");

                    if (!smeltingSlot.Locked)
                    {
                        return query;
                    }

                    int unlockPrice = SmeltingCalculator.CalculateUnlockPrice(slotIndex);

                    if (expectedPurchasePrice.ExpectedPurchasePrice != unlockPrice)
                    {
                        return query;
                    }

                    if (!profile.Rubies.Spend(unlockPrice))
                    {
                        return query;
                    }

                    smeltingSlot.Locked = false;

                    query.Update("smelting", playerId, smeltingSlots).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            return EarthJson(new Dictionary<string, object>(), new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    private static Types.Workshop.CraftingSlot CraftingSlotModelToResponseIncludingLocked(CraftingSlot craftingSlotModel, long currentTime, int streamVersion, int slotIndex)
    {
        if (craftingSlotModel.Locked)
        {
            return new Types.Workshop.CraftingSlot(null, null, null, null, 0, 0, 0, null, null, State.LOCKED, null, new UnlockPrice(CraftingCalculator.CalculateUnlockPrice(slotIndex), 0), streamVersion);
        }
        else
        {
            return CraftingSlotModelToResponse(craftingSlotModel, currentTime, streamVersion);
        }
    }

    private static Types.Workshop.CraftingSlot CraftingSlotModelToResponse(CraftingSlot craftingSlotModel, long currentTime, int streamVersion)
    {
        if (craftingSlotModel.Locked)
        {
            throw new ArgumentException($"{nameof(craftingSlotModel)} is locked.", nameof(craftingSlotModel));
        }

        CraftingSlot.ActiveJobR? activeJob = craftingSlotModel.ActiveJob;
        if (activeJob is not null)
        {
            CraftingCalculator.State state = CraftingCalculator.CalculateState(currentTime, activeJob, staticData.Catalog);
            return new Types.Workshop.CraftingSlot(
                activeJob.SessionId,
                activeJob.RecipeId,
                new OutputItem(state.Output.Id, state.Output.Count),
                [.. activeJob.Input.SelectMany(inputItems => inputItems).Select(item => new Types.Workshop.InputItem(
                    item.Id,
                    item.Count,
                    [.. item.Instances.Select(item => item.InstanceId)]
                ))],
                state.CompletedRounds,
                state.AvailableRounds,
                state.TotalRounds,
                !state.Completed ? TimeFormatter.FormatTime(state.NextCompletionTime) : null,
                !state.Completed ? TimeFormatter.FormatTime(state.TotalCompletionTime) : null,
                state.Completed ? State.COMPLETED : State.ACTIVE,
                null,
                null,
                streamVersion
            );
        }
        else
        {
            return new Types.Workshop.CraftingSlot(null, null, null, null, 0, 0, 0, null, null, State.EMPTY, null, null, streamVersion);
        }
    }

    private static Types.Workshop.SmeltingSlot SmeltingSlotModelToResponseIncludingLocked(SmeltingSlot smeltingSlotModel, long currentTime, int streamVersion, int slotIndex)
    {
        if (smeltingSlotModel.Locked)
        {
            return new Types.Workshop.SmeltingSlot(null, null, null, null, null, null, 0, 0, 0, null, null, State.LOCKED, null, new UnlockPrice(SmeltingCalculator.CalculateUnlockPrice(slotIndex), 0), streamVersion);
        }
        else
        {
            return SmeltingSlotModelToResponse(smeltingSlotModel, currentTime, streamVersion);
        }
    }

    private static Types.Workshop.SmeltingSlot SmeltingSlotModelToResponse(SmeltingSlot smeltingSlotModel, long currentTime, int streamVersion)
    {
        if (smeltingSlotModel.Locked)
        {
            throw new ArgumentException($"{nameof(smeltingSlotModel)} is locked.", nameof(smeltingSlotModel));
        }

        SmeltingSlot.ActiveJobR? activeJob = smeltingSlotModel.ActiveJob;
        if (activeJob is not null)
        {
            SmeltingCalculator.State state = SmeltingCalculator.CalculateState(currentTime, activeJob, smeltingSlotModel.Burning, staticData.Catalog);

            Types.Workshop.SmeltingSlot.FuelR? fuel;
            if (state.RemainingAddedFuel is not null && state.RemainingAddedFuel.Item.Count > 0)
            {
                fuel = new Types.Workshop.SmeltingSlot.FuelR(
                    new BurnRate(state.RemainingAddedFuel.BurnDuration, state.RemainingAddedFuel.HeatPerSecond),
                    state.RemainingAddedFuel.Item.Id,
                    state.RemainingAddedFuel.Item.Count,
                    [.. state.RemainingAddedFuel.Item.Instances.Select(item => item.InstanceId)]
                );
            }
            else
            {
                fuel = null;
            }

            var burning = new Types.Workshop.SmeltingSlot.BurningR(
                !state.Completed ? TimeFormatter.FormatTime(state.BurnStartTime) : null,
                !state.Completed ? TimeFormatter.FormatTime(state.BurnEndTime) : null,
                TimeFormatter.FormatDuration(state.RemainingHeat * 1000 / state.CurrentBurningFuel.HeatPerSecond),
                (float)state.CurrentBurningFuel.BurnDuration * state.CurrentBurningFuel.HeatPerSecond - state.RemainingHeat,
                new Types.Workshop.SmeltingSlot.FuelR(
                    new BurnRate(state.CurrentBurningFuel.BurnDuration, state.CurrentBurningFuel.HeatPerSecond),
                    state.CurrentBurningFuel.Item.Id,
                    state.CurrentBurningFuel.Item.Count,
                    [.. state.CurrentBurningFuel.Item.Instances.Select(item => item.InstanceId)]
                )
            );

            return new Types.Workshop.SmeltingSlot(
                fuel,
                burning,
                activeJob.SessionId,
                activeJob.RecipeId,
                new OutputItem(state.Output.Id, state.Output.Count),
                state.Input.Count > 0 ? [new Types.Workshop.InputItem(state.Input.Id, state.Input.Count, state.Input.Instances.Select(item => item.InstanceId).ToArray())] : [],
                state.CompletedRounds,
                state.AvailableRounds,
                state.TotalRounds,
                !state.Completed ? TimeFormatter.FormatTime(state.NextCompletionTime) : null,
                !state.Completed ? TimeFormatter.FormatTime(state.TotalCompletionTime) : null,
                state.Completed ? State.COMPLETED : State.ACTIVE,
                null,
                null,
                streamVersion
            );
        }
        else
        {
            SmeltingSlot.BurningR? burningModel = smeltingSlotModel.Burning;
            Types.Workshop.SmeltingSlot.BurningR? burning = burningModel is not null ? new Types.Workshop.SmeltingSlot.BurningR(
                null,
                null,
                TimeFormatter.FormatDuration(burningModel.RemainingHeat * 1000 / burningModel.Fuel.HeatPerSecond),
                (float)burningModel.Fuel.BurnDuration * burningModel.Fuel.HeatPerSecond * burningModel.Fuel.Item.Count - burningModel.RemainingHeat,
                new Types.Workshop.SmeltingSlot.FuelR(
                    new BurnRate(burningModel.Fuel.BurnDuration, burningModel.Fuel.HeatPerSecond),
                    burningModel.Fuel.Item.Id,
                    burningModel.Fuel.Item.Count,
                    [.. burningModel.Fuel.Item.Instances.Select(item => item.InstanceId)]
                )
            ) : null;
            return new Types.Workshop.SmeltingSlot(null, burning, null, null, null, null, 0, 0, 0, null, null, State.EMPTY, null, null, streamVersion);
        }
    }

    private sealed record StartRequestCrafting(
        string SessionId,
        string RecipeId,
        int Multiplier,
        StartRequestCrafting.Item[] Ingredients
    )
    {
        public sealed record Item(
            string ItemId,
            int Quantity,
            string[] ItemInstanceIds
        );
    }

    private sealed record StartRequestSmelting(
        string SessionId,
        string RecipeId,
        int Multiplier,
        StartRequestSmelting.Item Input,
        StartRequestSmelting.Item Fuel
    )
    {
        public sealed record Item(
            string ItemId,
            int Quantity,
            string[] ItemInstanceIds
        );
    }
}
