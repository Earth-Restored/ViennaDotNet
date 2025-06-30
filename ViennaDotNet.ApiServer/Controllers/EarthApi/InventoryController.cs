using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Inventory;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Common;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/inventory/survival")]
public class InventoryController : ControllerBase
{
    private static EarthDB earthDB => Program.DB;
    private static Catalog catalog => Program.staticData.Catalog;

    [HttpGet]
    public async Task<IActionResult> GetInventory(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        DB.Models.Player.Inventory inventoryModel;
        Hotbar hotbarModel;
        Journal journalModel;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("inventory", playerId, typeof(DB.Models.Player.Inventory))
                .Get("hotbar", playerId, typeof(Hotbar))
                .Get("journal", playerId, typeof(Journal))
                .ExecuteAsync(earthDB, cancellationToken);

            inventoryModel = results.Get<DB.Models.Player.Inventory>("inventory");
            hotbarModel = results.Get<Hotbar>("hotbar");
            journalModel = results.Get<Journal>("journal");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        Dictionary<string, int?> hotbarItemCounts = [];
        foreach (var item in hotbarModel.Items)
        {
            if (item is not null)
                hotbarItemCounts[item.Uuid] = hotbarItemCounts.GetOrDefault(item.Uuid, 0) + item.Count;
        }

        HashSet<string> hotbarItemInstances = [];
        foreach (var item in hotbarModel.Items)
        {
            if (item is not null && item.InstanceId is not null)
                hotbarItemInstances.Add(item.InstanceId);
        }

        var inventory = new Types.Inventory.Inventory(
            [.. hotbarModel.Items.Select(item => item is not null ? new HotbarItem(
                item.Uuid,
                item.Count,
                item.InstanceId,
                item.InstanceId is not null ? ItemWear.WearToHealth(item.Uuid, inventoryModel.GetItemInstance(item.Uuid, item.InstanceId)?.Wear ?? 0, catalog.ItemsCatalog) : 0.0f
                    ) : null)],
            [.. inventoryModel.StackableItems.Select(item =>
            {
                string uuid = item.Id;
                int count = item.Count - hotbarItemCounts.GetOrDefault(uuid, 0) ?? 0;
                Journal.ItemJournalEntry itemJournalEntry = journalModel.GetItem(uuid)!;
                string firstSeen = TimeFormatter.FormatTime(itemJournalEntry.FirstSeen);
                string lastSeen = TimeFormatter.FormatTime(itemJournalEntry.LastSeen);

                return new StackableInventoryItem(
                    uuid,
                    count,
                    1,
                    new StackableInventoryItem.OnR(firstSeen),
                    new StackableInventoryItem.OnR(lastSeen)
                );
            })],
            [.. inventoryModel.NonStackableItems.Select(item =>
            {
                string uuid = item.Id;
                Journal.ItemJournalEntry itemJournalEntry = journalModel.GetItem(uuid)!;
                string firstSeen = TimeFormatter.FormatTime(itemJournalEntry.FirstSeen);
                string lastSeen = TimeFormatter.FormatTime(itemJournalEntry.LastSeen);
                return new NonStackableInventoryItem(
                    uuid,
                    [.. item.Instances.Where(instance => !hotbarItemInstances.Contains(instance.InstanceId)).Select(instance => new NonStackableInventoryItem.Instance(instance.InstanceId, ItemWear.WearToHealth(item.Id, instance.Wear, catalog.ItemsCatalog)))],
                    1,
                    new NonStackableInventoryItem.OnR(firstSeen),
                    new NonStackableInventoryItem.OnR(lastSeen)
                );
            })]
        );

        string resp = Json.Serialize(new EarthApiResponse(inventory));
        return Content(resp, "application/json");
    }

    [HttpGet("hotbar")]
    public async Task<IActionResult> GetHotbar(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        SetHotbarRequestItem[]? setHotbarRequestItems = await Request.Body.AsJsonAsync<SetHotbarRequestItem[]>(cancellationToken);
        if (setHotbarRequestItems is null || setHotbarRequestItems.Length != 7)
        {
            return BadRequest();
        }

        DB.Models.Player.Inventory inventoryModel;
        Hotbar hotbarModel;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("inventory", playerId, typeof(DB.Models.Player.Inventory))
                .Then(results1 =>
                {
                    var hotbar = new Hotbar();
                    for (int index = 0; index < hotbar.Items.Length; index++)
                    {
                        SetHotbarRequestItem item = setHotbarRequestItems[index];
                        hotbar.Items[index] = item is not null ? new Hotbar.Item(item.Id, item.Count, item.InstanceId) : null;
                    }

                    hotbar.LimitToInventory(results1.Get<DB.Models.Player.Inventory>("inventory"));
                    return new EarthDB.Query(true)
                        .Update("hotbar", playerId, hotbar)
                        .Get("inventory", playerId, typeof(DB.Models.Player.Inventory))
                        .Get("hotbar", playerId, typeof(Hotbar));
                })
                .ExecuteAsync(earthDB, cancellationToken);

            inventoryModel = results.Get<DB.Models.Player.Inventory>("inventory");
            hotbarModel = results.Get<Hotbar>("hotbar");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        HotbarItem?[] hotbarItems = [.. hotbarModel.Items.Select(item => item is not null ? new HotbarItem(
            item.Uuid,
            item.Count,
            item.InstanceId,
            item.InstanceId is not null ? ItemWear.WearToHealth(item.Uuid, inventoryModel.GetItemInstance(item.Uuid, item.InstanceId)!.Wear, catalog.ItemsCatalog) : 0.0f
        ) : null)];

        string resp = Json.Serialize(hotbarItems);
        return Content(resp, "application/json");
    }

    [HttpPost("{itemId}/consume")]
    public async Task<IActionResult> ConsumeItem(string itemId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        Catalog.ItemsCatalogR.Item? item = catalog.ItemsCatalog.GetItem(itemId);

        if (item is null || item.ConsumeInfo is null)
        {
            return BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("inventory", playerId, typeof(DB.Models.Player.Inventory))
                .Get("journal", playerId, typeof(Journal))
                .Get("profile", playerId, typeof(Profile))
                .Get("boosts", playerId, typeof(Boosts))
                .Then(results1 =>
                {
                    DB.Models.Player.Inventory inventory = results1.Get<DB.Models.Player.Inventory>("inventory");
                    Journal journal = results1.Get<Journal>("journal");
                    Profile profile = results1.Get<Profile>("profile");
                    Boosts boosts = results1.Get<Boosts>("boosts");

                    var query = new EarthDB.Query(true);

                    if (!inventory.TakeItems(itemId, 1))
                    {
                        return new EarthDB.Query(false);
                    }

                    string? returnItemId = item.ConsumeInfo.ReturnItemId;
                    if (returnItemId is not null)
                    {
                        Catalog.ItemsCatalogR.Item? returnItem = catalog.ItemsCatalog.GetItem(returnItemId);
                        Debug.Assert(returnItem is not null);

                        if (returnItem.Stackable)
                        {
                            inventory.AddItems(returnItemId, 1);
                        }
                        else
                        {
                            inventory.AddItems(returnItemId, [new NonStackableItemInstance(U.RandomUuid().ToString(), 0)]);
                        }

                        if (journal.AddCollectedItem(returnItemId, requestStartedOn, 1) == 0)
                        {
                            if (returnItem.JournalEntry is not null)
                            {
                                query.Then(TokenUtils.AddToken(playerId, new Tokens.JournalItemUnlockedToken(returnItemId)));
                            }
                        }
                    }

                    int healing = item.ConsumeInfo.Heal;

                    int healingMultiplier = BoostUtils.GetActiveStatModifiers(boosts, requestStartedOn, catalog.ItemsCatalog).FoodMultiplier;
                    if (healingMultiplier > 0)
                    {
                        healing = healing * (healingMultiplier + 100) / 100;
                    }

                    int maxPlayerHealth = BoostUtils.GetMaxPlayerHealth(boosts, requestStartedOn, catalog.ItemsCatalog);
                    profile.Health += healing;
                    if (profile.Health > maxPlayerHealth)
                    {
                        profile.Health = maxPlayerHealth;
                    }

                    query.Update("inventory", playerId, inventory).Update("journal", playerId, journal).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = Json.Serialize(new EarthApiResponse(null, new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }
    }

    private sealed record SetHotbarRequestItem(
        string Id,
        int Count,
        string? InstanceId
    );
}
