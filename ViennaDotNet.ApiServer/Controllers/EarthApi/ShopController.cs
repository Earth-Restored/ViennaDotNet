using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Buildplates;
using ViennaDotNet.ApiServer.Types.Shop;
using ViennaDotNet.BuildplateImporter;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.ObjectStore.Client;
using ViennaDotNet.StaticData;
using Buildplates = ViennaDotNet.DB.Models.Player.Buildplates;

namespace ViennaDotNet.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/commerce")]
public class ShopController : ViennaControllerBase
{
    private static Catalog catalog => Program.staticData.Catalog;
    private static EarthDB earthDB => Program.DB;
    private static ObjectStoreClient objectStoreClient => Program.objectStore;
    private static Importer importer => Program.importer;

    private sealed record StoreItemInfoRequest(string Id, string StoreItemType, uint StreamVersion);

    [HttpPost("storeItemInfo")]
    public async Task<IActionResult> GetStoreItemInfo(CancellationToken cancellationToken)
    {
        var request = await Request.Body.AsJsonAsync<StoreItemInfoRequest[]>(cancellationToken);

        if (request is null || request.Length == 0)
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

                        byte[]? previewData = await objectStoreClient.Get(buildplate.PreviewObjectId).Task as byte[];

                        if (previewData is null)
                        {
                            Log.Warning($"Failed to get preview for buildplate {item.Id}");
                            result.Add(new StoreItemInfo(itemId, storeItemType, StoreItemInfo.StoreItemStatus.NotFound, item.StreamVersion, null, null, null, null, null));
                            break;
                        }

                        string model = Encoding.ASCII.GetString(previewData);

                        var itemFromMap = catalog.ShopCatalog.Items.GetValueOrDefault(itemId);

                        result.Add(new StoreItemInfo(
                            itemId,
                            storeItemType,
                            StoreItemInfo.StoreItemStatus.Found,
                            item.StreamVersion,
                            model,
                            new Offset(0, buildplate.Offset, 0),
                            new Dimension(buildplate.Size, buildplate.Size),
                            itemFromMap?.ItemCounts,
                            itemFromMap?.FeaturedItem));
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
    public async Task<IActionResult> Purchase(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        var request = await Request.Body.AsJsonAsync<PurchaseItemRequest>(cancellationToken);

        if (request is null)
        {
            return BadRequest();
        }

        var rubies = await ProcessPurchase(playerId, request.ItemId, request.ExpectedPurchasePrice, cancellationToken);

        if (rubies is not { } rubiesVal)
        {
            return BadRequest();
        }

        return EarthJson(rubiesVal.Purchased + rubiesVal.Earned);
    }

    [HttpPost("purchaseV2")]
    public async Task<IActionResult> PurchaseV2(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        var request = await Request.Body.AsJsonAsync<PurchaseItemRequest>(cancellationToken);

        if (request is null)
        {
            return BadRequest();
        }

        var rubies = await ProcessPurchase(playerId, request.ItemId, request.ExpectedPurchasePrice, cancellationToken);

        if (rubies is not { } rubiesVal)
        {
            return BadRequest();
        }

        return EarthJson(new Types.Profile.SplitRubies(rubiesVal.Purchased, rubiesVal.Earned));
    }

    private static async Task<(int Purchased, int Earned)?> ProcessPurchase(string playerId, Guid itemId, int expectedPurchasePrice, CancellationToken cancellationToken)
    {
        // TODO: when playfab is implemented, validate expectedPurchasePrice (price is stored in playfab)
        if (!catalog.ShopCatalog.Items.TryGetValue(itemId, out var itemToPurchase))
        {
            Log.Warning($"Player {playerId} tried to purchase unknown item '{itemId}'");
            return null;
        }

        try
        {
            var results = await new EarthDB.Query(true)
                .Get("profile", playerId, typeof(Profile))
                .Get("inventory", playerId, typeof(Inventory))
                .Get("buildplates", playerId, typeof(Buildplates))
                .Then(async results =>
                {
                    var profile = results.Get<Profile>("profile");
                    var inventory = results.Get<Inventory>("inventory");
                    var buildplates = results.Get<Buildplates>("buildplates");

                    if (profile.Rubies.Total < expectedPurchasePrice)
                    {
                        Log.Warning($"Player {playerId} tried to purchase item '{itemId}' but does not have enough rubies");
                        return EarthDB.Query.Empty;
                    }

                    var query = new EarthDB.Query(true);

                    switch (itemToPurchase.ItemType)
                    {
                        case Catalog.ShopCatalogR.StoreItemInfo.StoreItemType.Buildplates:
                            {
                                string? buidplateId = await importer.AddBuidplateToPlayer(itemToPurchase.Id.ToString(), playerId, cancellationToken);

                                if (string.IsNullOrEmpty(buidplateId))
                                {
                                    Log.Warning($"Failed to add buildplate {itemToPurchase.Id} to player {playerId}");
                                    return query;
                                }
                            }

                            break;
                        case Catalog.ShopCatalogR.StoreItemInfo.StoreItemType.Items:
                            {
                                if (itemToPurchase.ItemCounts is not { Count: > 0 })
                                {
                                    return query;
                                }

                                foreach (var item in itemToPurchase.ItemCounts)
                                {
                                    inventory.AddItems(item.Key.ToString(), item.Value);
                                }

                                query.Update("inventory", playerId, inventory);
                            }

                            break;

                        default:
                            Log.Warning($"Shop item '{itemId}' has unknown {nameof(Catalog.ShopCatalogR.StoreItemInfo.StoreItemType)}");
                            break;
                    }

                    bool spent = !profile.Rubies.Spend(expectedPurchasePrice);
                    Debug.Assert(spent);

                    query.Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        return (0, 0);
    }
}
