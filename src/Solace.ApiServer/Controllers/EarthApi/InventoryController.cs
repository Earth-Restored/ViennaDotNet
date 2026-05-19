using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;
using Solace.ApiServer.Exceptions;
using Solace.ApiServer.Types.Inventory;
using Solace.ApiServer.Utils;
using Solace.Common;
using Solace.Common.Utils;
using Solace.DB;
using Solace.DB.Models.Common;
using Solace.DB.Models.Player;
using Solace.StaticData;
using Microsoft.EntityFrameworkCore;
using Solace.DB.Utils;

namespace Solace.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/inventory/survival")]
internal sealed class InventoryController : SolaceControllerBase
{
    private readonly EarthDbContext _earthDB;
    private readonly Catalog _catalog;

    public InventoryController(EarthDbContext earthDB, StaticData.StaticData staticData)
    {
        _earthDB = earthDB;
        _catalog = staticData.Catalog;
    }

    [HttpGet]
    public async Task<Results<ContentHttpResult, BadRequest>> GetInventory(CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId))
        {
            return TypedResults.BadRequest();
        }

        var inventory = await _earthDB.Inventories
            .AsNoTracking()
            .FirstOrNewAsync(inventory => inventory.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        var hotbar = await _earthDB.Hotbars
            .AsNoTracking()
            .FirstOrNewAsync(hotbar => hotbar.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        var journal = await _earthDB.Journals
            .AsNoTracking()
            .FirstOrNewAsync(journal => journal.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        Dictionary<string, int?> hotbarItemCounts = [];
        foreach (var item in hotbar.Items)
        {
            if (item is not null)
            {
                hotbarItemCounts[item.Uuid] = hotbarItemCounts.GetOrDefault(item.Uuid, 0) + item.Count;
            }
        }

        HashSet<string> hotbarItemInstances = [];
        foreach (var item in hotbar.Items)
        {
            if (item is not null && item.InstanceId is not null)
            {
                hotbarItemInstances.Add(item.InstanceId);
            }
        }

        var inventoryResponse = new Types.Inventory.Inventory(
            [.. hotbar.Items.Select(item => item is not null ? new HotbarItem(
                item.Uuid,
                item.Count,
                item.InstanceId,
                item.InstanceId is not null ? ItemWear.WearToHealth(item.Uuid, inventory.GetItemInstance(item.Uuid, item.InstanceId)?.Wear ?? 0, _catalog.ItemsCatalog) : 0.0f
                    ) : null)],
            [.. inventory.StackableItems.Select(item =>
            {
                string uuid = item.Id;
                int count = item.Count - hotbarItemCounts.GetOrDefault(uuid, 0) ?? 0;
                JournalEF.ItemJournalEntry itemJournalEntry = journal.GetItem(uuid)!;
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
            [.. inventory.NonStackableItems.Select(item =>
            {
                string uuid = item.Id;
                JournalEF.ItemJournalEntry itemJournalEntry = journal.GetItem(uuid)!;
                string firstSeen = TimeFormatter.FormatTime(itemJournalEntry.FirstSeen);
                string lastSeen = TimeFormatter.FormatTime(itemJournalEntry.LastSeen);
                return new NonStackableInventoryItem(
                    uuid,
                    [.. item.Instances.Where(instance => !hotbarItemInstances.Contains(instance.InstanceId)).Select(instance => new NonStackableInventoryItem.Instance(instance.InstanceId, ItemWear.WearToHealth(item.Id, instance.Wear, _catalog.ItemsCatalog)))],
                    1,
                    new NonStackableInventoryItem.OnR(firstSeen),
                    new NonStackableInventoryItem.OnR(lastSeen)
                );
            })]
        );

        string resp = Json.Serialize(new EarthApiResponse(inventoryResponse));
        return TypedResults.Content(resp, "application/json");
    }

    [HttpPut("hotbar")]
    public async Task<Results<BadRequest, ContentHttpResult>> SetHotbar(CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId))
        {
            return TypedResults.BadRequest();
        }

        SetHotbarRequestItem[]? setHotbarRequestItems = await Request.Body.AsJsonAsync<SetHotbarRequestItem[]>(cancellationToken);
        if (setHotbarRequestItems is null || setHotbarRequestItems.Length != 7)
        {
            return TypedResults.BadRequest();
        }

        var inventory = await _earthDB.Inventories
            .AsNoTracking()
            .FirstOrNewAsync(inventory => inventory.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        var hotbar = await _earthDB.Hotbars
            .AsTracking()
            .FirstOrNewAsync(hotbar => hotbar.Id == accountId, cancellationToken: cancellationToken);

        for (int index = 0; index < hotbar.Items.Length; index++)
        {
            SetHotbarRequestItem item = setHotbarRequestItems[index];
            hotbar.Items[index] = item is not null ? new HotbarEF.Item(item.Id, item.Count, item.InstanceId) : null;
        }

        hotbar.LimitToInventory(inventory);

        await _earthDB.SaveChangesAsync(cancellationToken);

        HotbarItem?[] hotbarItems = [.. hotbar.Items.Select(item => item is not null ? new HotbarItem(
            item.Uuid,
            item.Count,
            item.InstanceId,
            item.InstanceId is not null ? ItemWear.WearToHealth(item.Uuid, inventory.GetItemInstance(item.Uuid, item.InstanceId)!.Wear, _catalog.ItemsCatalog) : 0.0f
        ) : null)];

        string resp = Json.Serialize(hotbarItems);
        return TypedResults.Content(resp, "application/json");
    }

    [HttpPost("{itemId}/consume")]
    public async Task<Results<ContentHttpResult, BadRequest>> ConsumeItem(string itemId, CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId))
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        Catalog.ItemsCatalogR.Item? item = _catalog.ItemsCatalog.GetItem(itemId);

        if (item is null || item.ConsumeInfo is null)
        {
            return TypedResults.BadRequest();
        }

        var inventory = await _earthDB.Inventories
           .AsTracking()
           .FirstOrNewAsync(inventory => inventory.Id == accountId, cancellationToken: cancellationToken);

        var journal = await _earthDB.Journals
            .AsTracking()
            .FirstOrNewAsync(journal => journal.Id == accountId, cancellationToken: cancellationToken);

        var profile = await _earthDB.Profiles
            .AsTracking()
            .FirstOrNewAsync(profile => profile.Id == accountId, cancellationToken: cancellationToken);

        var boosts = await _earthDB.Boosts
            .AsNoTracking()
            .FirstOrNewAsync(boosts => boosts.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        if (!inventory.TakeItems(itemId, 1))
        {
            return TypedResults.Content(Json.Serialize(new EarthApiResponse(null, null)), "application/json");
        }

        var results = new EarthDbContext.Results(_earthDB);

        string? returnItemId = item.ConsumeInfo.ReturnItemId;
        if (returnItemId is not null)
        {
            Catalog.ItemsCatalogR.Item? returnItem = _catalog.ItemsCatalog.GetItem(returnItemId);
            Debug.Assert(returnItem is not null);

            if (returnItem.Stackable)
            {
                inventory.AddItems(returnItemId, 1);
            }
            else
            {
                inventory.AddItems(returnItemId, [new NonStackableItemInstance(Guid.NewGuid().ToString(), 0)]);
            }

            if (journal.AddCollectedItem(returnItemId, requestStartedOn, 1) == 0)
            {
                if (returnItem.JournalEntry is not null)
                {
                    await TokenUtils.AddTokenAsync(results, accountId, new TokensEF.JournalItemUnlockedToken(returnItemId));
                }
            }
        }

        int healing = item.ConsumeInfo.Heal;

        int healingMultiplier = BoostUtils.GetActiveStatModifiers(boosts, requestStartedOn, _catalog.ItemsCatalog).FoodMultiplier;
        if (healingMultiplier > 0)
        {
            healing = healing * (healingMultiplier + 100) / 100;
        }

        int maxPlayerHealth = BoostUtils.GetMaxPlayerHealth(boosts, requestStartedOn, _catalog.ItemsCatalog);
        profile.Health += healing;
        if (profile.Health > maxPlayerHealth)
        {
            profile.Health = maxPlayerHealth;
        }

        await _earthDB.SaveChangesAsync(cancellationToken);

        results.Inventory = inventory.Version;
        results.Journal = journal.Version;
        results.Profile = profile.Version;

        string resp = Json.Serialize(new EarthApiResponse(null, new EarthApiResponse.UpdatesResponse(results)));
        return TypedResults.Content(resp, "application/json");
    }

    private sealed record SetHotbarRequestItem(
        string Id,
        int Count,
        string? InstanceId
    );
}
