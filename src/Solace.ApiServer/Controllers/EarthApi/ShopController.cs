using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Solace.ApiServer.Exceptions;
using Solace.ApiServer.Types.Buildplates;
using Solace.ApiServer.Types.Shop;
using Solace.BuildplateImporter;
using Solace.Common.Utils;
using Solace.DB;
using Solace.DB.Models.Player;
using Solace.ObjectStore.Client;
using Solace.StaticData;

namespace Solace.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/commerce")]
internal sealed class ShopController : SolaceControllerBase
{
    private static StaticData.StaticData staticData => Program.staticData;
    private static EarthDB earthDB => Program.DB;
    private static Importer importer => Program.importer;

    private sealed record StoreItemInfoRequest(string Id, string StoreItemType, uint StreamVersion);

    [HttpPost("storeItemInfo")]
    public async Task<ContentHttpResult> GetStoreItemInfo(CancellationToken cancellationToken)
    {
        var request = await Request.Body.AsJsonAsync<StoreItemInfoRequest[]>(cancellationToken);

        if (request is null or { Length: 0 })
        {
            return EarthJson(Array.Empty<StoreItemInfo>());
        }

        List<StoreItemInfo> result = new(request.Length);

        EarthDB.ObjectResults buildplateResults;
        try
        {
            buildplateResults = await new EarthDB.ObjectQuery(false)
                .GetBuildplates(request.Where(item => item.StoreItemType == "Buildplates").Select(item => item.Id))
                .ExecuteAsync(earthDB, cancellationToken);
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        foreach (var item in request)
        {
            switch (item.StoreItemType)
            {
                case "Buildplates":
                    {
                        var buildplate = buildplateResults.GetBuildplate(item.Id);

                        var itemId = Guid.Parse(item.Id);
                        StoreItemInfo.StoreItemTypeE storeItemType = Enum.Parse<StoreItemInfo.StoreItemTypeE>(item.StoreItemType);

                        if (buildplate is null)
                        {
                            Log.Warning($"Buildplate with id {item.Id} not found");
                            result.Add(new StoreItemInfo(itemId, storeItemType, StoreItemInfo.StoreItemStatus.NotFound, item.StreamVersion, null, null, null, null, null));
                            break;
                        }

                        await using var objectStoreClient = await Program.GetObjectStoreClient();

                        byte[]? previewData = await objectStoreClient.GetAsync(buildplate.PreviewObjectId);

                        if (previewData is null)
                        {
                            Log.Warning($"Failed to get preview for buildplate {item.Id}");
                            result.Add(new StoreItemInfo(itemId, storeItemType, StoreItemInfo.StoreItemStatus.NotFound, item.StreamVersion, null, null, null, null, null));
                            break;
                        }

                        string model = Encoding.ASCII.GetString(previewData);

                        //var itemFromMap = staticData.Catalog.ShopCatalog.Items.GetValueOrDefault(itemId);

                        result.Add(new StoreItemInfo(
                            itemId,
                            storeItemType,
                            StoreItemInfo.StoreItemStatus.Found,
                            item.StreamVersion,
                            model,
                            new Offset(0, buildplate.Offset, 0),
                            new Dimension(buildplate.Size, buildplate.Size),
                            null,
                            null));
                    }

                    break;
            }
        }

        return EarthJson(result);
    }

    private sealed record PurchaseItemRequest(
        int ExpectedPurchasePrice,
        Guid ItemId
    );

    [HttpPost("purchase")]
    public async Task<Results<ContentHttpResult, BadRequest>> Purchase(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        var request = await Request.Body.AsJsonAsync<PurchaseItemRequest>(cancellationToken);

        if (request is null)
        {
            return TypedResults.BadRequest();
        }

        var rubies = await ProcessPurchase(playerId, request.ItemId, request.ExpectedPurchasePrice, cancellationToken);

        if (rubies is not { } rubiesVal)
        {
            return TypedResults.BadRequest();
        }

        return EarthJson(rubiesVal.Purchased + rubiesVal.Earned);
    }

    [HttpPost("purchaseV2")]
    public async Task<Results<ContentHttpResult, BadRequest>> PurchaseV2(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        var request = await Request.Body.AsJsonAsync<PurchaseItemRequest>(cancellationToken);

        if (request is null)
        {
            return TypedResults.BadRequest();
        }

        var rubies = await ProcessPurchase(playerId, request.ItemId, request.ExpectedPurchasePrice, cancellationToken);

        if (rubies is not { } rubiesVal)
        {
            return TypedResults.BadRequest();
        }

        return EarthJson(new Types.Profile.SplitRubies(rubiesVal.Purchased, rubiesVal.Earned));
    }

    private static async Task<(int Purchased, int Earned)?> ProcessPurchase(string playerId, Guid itemId, int expectedPurchasePrice, CancellationToken cancellationToken)
    {
        if (!staticData.Playfab.Items.TryGetValue(itemId, out var itemToPurchase))
        {
            Log.Debug($"Player {playerId} tried to purchase unknown item '{itemId}' (playfab)");
            return null;
        }

        int? playfabPrice = itemToPurchase.Data switch
        {
            Playfab.Item.BuildplateData data => data.Cost,
            Playfab.Item.InventoryItemData data => data.Cost,
            _ => null,
        };

        if (playfabPrice is not { } actualPurchasePrice)
        {
            return null;
        }

        // TODO: do this or just use actualPurchasePrice?
        if (expectedPurchasePrice != actualPurchasePrice)
        {
            return null;
        }

        try
        {
            Rubies? rubies = null;
            switch (itemToPurchase.Data)
            {
                case Playfab.Item.BuildplateData data:
                    {
                        // TODO: the amount of rubies could change between the 2 db calls, if multiple people were using the same account at the same time, but that's unlikely and AddBuidplateToPlayer cannot be inside a rw query as it also writes and there can only be 1 rw connection for a file database at a time
                        var profile = (await new EarthDB.Query(false)
                           .Get("profile", playerId, typeof(Profile))
                           .ExecuteAsync(earthDB, cancellationToken))
                           .Get<Profile>("profile");

                        if (profile.Rubies.Total < expectedPurchasePrice)
                        {
                            Log.Debug($"Player {playerId} tried to purchase item '{itemId}' but does not have enough rubies");
                            break;
                        }

                        string? buidplateId = await importer.AddBuidplateToPlayer(data.Id.ToString(), playerId, cancellationToken);

                        if (string.IsNullOrEmpty(buidplateId))
                        {
                            Log.Warning($"Failed to add buildplate {data.Id} to player {playerId}");
                            break;
                        }

                        bool spent = profile.Rubies.Spend(expectedPurchasePrice);
                        Debug.Assert(spent);

                        await new EarthDB.Query(true)
                           .Update("profile", playerId, profile)
                           .ExecuteAsync(earthDB, cancellationToken);

                        rubies = profile.Rubies;
                    }

                    break;
                case Playfab.Item.InventoryItemData data:
                    {
                        var results = await new EarthDB.Query(true)
                           .Get("profile", playerId, typeof(Profile))
                           .Get("journal", playerId, typeof(Journal))
                           .Get("inventory", playerId, typeof(Inventory))
                           .Then(results =>
                           {
                               var profile = results.Get<Profile>("profile");
                               var journal = results.Get<Journal>("journal");
                               var inventory = results.Get<Inventory>("inventory");

                               if (profile.Rubies.Total < expectedPurchasePrice)
                               {
                                   Log.Debug($"Player {playerId} tried to purchase item '{itemId}' but does not have enough rubies");
                                   return EarthDB.Query.Empty;
                               }

                               var query = new EarthDB.Query(true);

                               inventory.AddItems(data.Id.ToString(), data.Amount);
                               journal.AddCollectedItem(data.Id.ToString(), U.CurrentTimeMillis(), data.Amount);

                               query.Update("inventory", playerId, inventory);
                               query.Update("journal", playerId, journal);
                               // TODO: add to activity log?

                               bool spent = profile.Rubies.Spend(expectedPurchasePrice);
                               Debug.Assert(spent);

                               query.Update("profile", playerId, profile);
                               query.Extra("rubies", profile.Rubies);

                               return query;
                           })
                           .ExecuteAsync(earthDB, cancellationToken);

                        rubies = results.GetExtra("rubies") as Rubies;
                    }

                    break;

                default:
                    Log.Warning($"Shop item '{itemId}' has unknown {nameof(Playfab.Item.ItemData)}");
                    break;
            }

            if (rubies is null)
            {
                return null;
            }

            return (rubies.Purchased, rubies.Earned);
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }
}
