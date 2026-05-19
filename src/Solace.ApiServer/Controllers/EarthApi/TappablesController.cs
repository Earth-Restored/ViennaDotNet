using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Solace.ApiServer.Exceptions;
using Solace.ApiServer.Types.Common;
using Solace.ApiServer.Types.Tappables;
using Solace.ApiServer.Utils;
using Solace.Common.Utils;
using Solace.DB;
using Solace.DB.Models.Player;
using Solace.StaticData;
using Microsoft.EntityFrameworkCore;
using Solace.DB.Utils;

namespace Solace.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
internal sealed class TappablesController : SolaceControllerBase
{
    private readonly TappablesManager _tappablesManager;
    private readonly EarthDbContext _earthDB;
    private readonly StaticData.StaticData _staticData;

    public TappablesController(TappablesManager tappablesManager, EarthDbContext earthDb, StaticData.StaticData staticData)
    {
        _tappablesManager = tappablesManager;
        _earthDB = earthDb;
        _staticData = staticData;
    }

    [HttpGet("locations/{lat}/{lon}")]
    public async Task<Results<ContentHttpResult, BadRequest>> GetTappables(double lat, double lon, CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId))
        {
            return TypedResults.BadRequest();
        }

        long requestStartedOn = HttpContext.GetTimestamp();

        await _tappablesManager.NotifyTileActiveAsync(accountId, lat, lon);

        TappablesManager.Tappable[] tappables = _tappablesManager.GetTappablesAround(lat, lon, 5.0);    // TODO: radius
        TappablesManager.Encounter[] encounters = _tappablesManager.GetEncountersAround(lat, lon, 5.0);    // TODO: radius

        var redeemedTappables = await _earthDB.RedeemedTappables
            .AsNoTracking()
            .FirstOrNewAsync(redeemedTappables => redeemedTappables.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        IEnumerable<ActiveLocation> activeLocationTappables = tappables
            .Where(tappable => tappable.SpawnTime + tappable.ValidFor > requestStartedOn && !redeemedTappables.IsRedeemed(tappable.Id))
            .Select(tappable => new ActiveLocation(
                tappable.Id,
                TappablesManager.LocationToTileId(tappable.Lat, tappable.Lon),
                new Coordinate(tappable.Lat, tappable.Lon),
                TimeFormatter.FormatTime(tappable.SpawnTime),
                TimeFormatter.FormatTime(tappable.SpawnTime + tappable.ValidFor),
                ActiveLocation.TypeE.TAPPABLE,
                tappable.Icon,
                new ActiveLocation.MetadataR(Guid.NewGuid().ToString(), Enum.Parse<Rarity>(tappable.Rarity.ToString())),
                new ActiveLocation.TappableMetadataR(Enum.Parse<Rarity>(tappable.Rarity.ToString())),
                null
            ));

        IEnumerable<ActiveLocation> activeLocationEncounters = encounters
            .Where(encounter => encounter.SpawnTime + encounter.ValidFor > requestStartedOn)
            .Select(encounter => new ActiveLocation(
                encounter.Id,
                TappablesManager.LocationToTileId(encounter.Lat, encounter.Lon),
                new Coordinate(encounter.Lat, encounter.Lon),
                TimeFormatter.FormatTime(encounter.SpawnTime),
                TimeFormatter.FormatTime(encounter.SpawnTime + encounter.ValidFor),
                ActiveLocation.TypeE.ENCOUNTER,
                encounter.Icon,
                new ActiveLocation.MetadataR(Guid.NewGuid().ToString(), Enum.Parse<Rarity>(encounter.Rarity.ToString())),
                null,
                new ActiveLocation.EncounterMetadataR(
                    ActiveLocation.EncounterMetadataR.EncounterTypeE.SHORT_4X4_PEACEFUL,    // TODO
                                                                                            //UUID.randomUUID().toString(),    // TODO: what is this field for and does it matter what we put here?
                    encounter.Id,
                    encounter.EncounterBuildplateId,
                    ActiveLocation.EncounterMetadataR.AnchorStateE.OFF,
                    "",
                    ""
                )
            ));

        ActiveLocation[] activeLocations = [.. activeLocationTappables, .. activeLocationEncounters];

        return EarthJson(new Dictionary<string, object>()
        {
            { "killSwitchedTileIds", new List<object>() },
            { "activeLocations", activeLocations }
        });
    }

    [HttpPost("tappables/{tileId}")]
    public async Task<Results<ContentHttpResult, BadRequest>> RedeemTappable(string tileId, CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId))
        {
            return TypedResults.BadRequest();
        }

        TappableRequest? tappableRequest = await Request.Body.AsJsonAsync<TappableRequest>(cancellationToken);
        if (tappableRequest is null)
        {
            return TypedResults.BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        TappablesManager.Tappable? tappable = _tappablesManager.GetTappableWithId(tappableRequest.Id, tileId);
        if (tappable is null || !_tappablesManager.IsTappableValidFor(tappable, requestStartedOn, tappableRequest.PlayerCoordinate.Latitude, tappableRequest.PlayerCoordinate.Longitude))
        {
            return TypedResults.BadRequest();
        }

        var redeemedTappables = await _earthDB.RedeemedTappables
            .AsTracking()
            .FirstOrNewAsync(redeemedTappables => redeemedTappables.Id == accountId, cancellationToken: cancellationToken);

        var boosts = await _earthDB.Boosts
            .AsNoTracking()
            .FirstOrNewAsync(boosts => boosts.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        if (redeemedTappables.IsRedeemed(tappable.Id))
        {
            return TypedResults.BadRequest();
        }

        int experiencePointsGlobalMultiplier = 0;

        Dictionary<string, int> experiencePointsPerItemMultiplier = [];
        foreach (var effect in BoostUtils.GetActiveEffects(boosts, requestStartedOn, _staticData.Catalog.ItemsCatalog))
        {
            if (effect.Type is Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.TypeE.ITEM_XP)
            {
                if (effect.ApplicableItemIds is not null && effect.ApplicableItemIds.Length > 0)
                {
                    foreach (string itemId in effect.ApplicableItemIds)
                    {
                        experiencePointsPerItemMultiplier[itemId] = experiencePointsPerItemMultiplier.GetValueOrDefault(itemId) + effect.Value;
                    }
                }
                else
                {
                    experiencePointsGlobalMultiplier += effect.Value;
                }
            }
        }

        var rewards = new Utils.Rewards();

        foreach (TappablesManager.Tappable.Item item in tappable.Items)
        {
            rewards.AddItem(item.Id, item.Count);
            int experiencePoints = _staticData.Catalog.ItemsCatalog.GetItem(item.Id)!.Experience.Tappable;
            int experiencePointsMultiplier = experiencePointsGlobalMultiplier + experiencePointsPerItemMultiplier.GetValueOrDefault(item.Id);
            if (experiencePointsMultiplier > 0)
            {
                experiencePoints = experiencePoints * (experiencePointsMultiplier + 100) / 100;
            }

            rewards.AddExperiencePoints(experiencePoints * item.Count);
        }

        rewards.AddRubies(1); // TODO

        redeemedTappables.Add(tappable.Id, tappable.SpawnTime + tappable.ValidFor);
        redeemedTappables.Prune(requestStartedOn);

        await _earthDB.SaveChangesAsync(cancellationToken);
        var results = new EarthDbContext.Results(_earthDB);

        await ActivityLogUtils.AddEntryAsync(results, accountId, new ActivityLogEF.TappableEntry(requestStartedOn, rewards.ToDBRewardsModel()));
        await rewards.ToRedeemQueryAsync(results, accountId, requestStartedOn, _staticData);

        return EarthJson(new Dictionary<string, object?>()
        {
            { "token", new Token(
                Token.Type.TAPPABLE,
                [],
                rewards.ToApiResponse(),
                Token.LifetimeE.PERSISTENT
            ) },
            { "updates", null }
        }, new EarthApiResponse.UpdatesResponse(results));
    }

    [HttpPost("multiplayer/encounters/state")]
    public async Task<Results<ContentHttpResult, BadRequest>> EncountersState(CancellationToken cancellationToken)
    {
        var requestedIds = await Request.Body.AsJsonAsync<Dictionary<string, object>>(cancellationToken);

        if (requestedIds is null)
        {
            return TypedResults.BadRequest();
        }

        foreach (var entry in requestedIds)
        {
            if (entry.Value is not string)
            {
                return TypedResults.BadRequest();
            }
        }

        // TODO

        var encounterStates = new Dictionary<string, EncounterState>();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
        foreach (var (encounterId, tileId) in requestedIds)
        {
            encounterStates[encounterId] = new EncounterState(EncounterState.ActiveEncounterStateE.PRISTINE);
        }
#pragma warning restore IDE0059 // Unnecessary assignment of a value

        return EarthJson(encounterStates);
    }

    private sealed record TappableRequest(
        Guid Id,
        Coordinate PlayerCoordinate
    );
}
