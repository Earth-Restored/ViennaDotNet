using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Types.Tappables;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
public class TappablesController : ViennaControllerBase
{
    private static TappablesManager tappablesManager => Program.tappablesManager;
    private static EarthDB earthDB => Program.DB;
    private static StaticData.StaticData staticData => Program.staticData;

    [HttpGet("locations/{lat}/{lon}")]
    public async Task<Results<ContentHttpResult, BadRequest>> GetTappables(double lat, double lon, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        long requestStartedOn = HttpContext.GetTimestamp();

        await tappablesManager.NotifyTileActiveAsync(playerId, lat, lon);

        TappablesManager.Tappable[] tappables = tappablesManager.GetTappablesAround(lat, lon, 5.0);    // TODO: radius
        TappablesManager.Encounter[] encounters = tappablesManager.GetEncountersAround(lat, lon, 5.0);    // TODO: radius

        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("redeemedTappables", playerId, typeof(RedeemedTappables))
                .ExecuteAsync(earthDB, cancellationToken);
            RedeemedTappables redeemedTappables = results.Get<RedeemedTappables>("redeemedTappables");

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
                    new ActiveLocation.MetadataR(U.RandomUuid().ToString(), Enum.Parse<Rarity>(tappable.Rarity.ToString())),
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
                    new ActiveLocation.MetadataR(U.RandomUuid().ToString(), Enum.Parse<Rarity>(encounter.Rarity.ToString())),
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
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("tappables/{tileId}")]
    public async Task<Results<ContentHttpResult, BadRequest>> RedeemTappable(string tileId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
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

        TappablesManager.Tappable? tappable = tappablesManager.GetTappableWithId(tappableRequest.Id, tileId);
        if (tappable is null || !tappablesManager.IsTappableValidFor(tappable, requestStartedOn, tappableRequest.PlayerCoordinate.Latitude, tappableRequest.PlayerCoordinate.Longitude))
        {
            return TypedResults.BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("redeemedTappables", playerId, typeof(RedeemedTappables))
                .Get("boosts", playerId, typeof(Boosts))
                .Then(results1 =>
                {
                    var query = new EarthDB.Query(true);
                    Boosts boosts = results1.Get<Boosts>("boosts");

                    RedeemedTappables redeemedTappables = results1.Get<RedeemedTappables>("redeemedTappables");

                    if (redeemedTappables.IsRedeemed(tappable.Id))
                    {
                        query.Extra("success", false);
                        return query;
                    }

                    int experiencePointsGlobalMultiplier = 0;

                    Dictionary<string, int> experiencePointsPerItemMultiplier = [];
                    foreach (var effect in BoostUtils.GetActiveEffects(boosts, requestStartedOn, staticData.Catalog.ItemsCatalog))
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
                        int experiencePoints = staticData.Catalog.ItemsCatalog.GetItem(item.Id)!.Experience.Tappable;
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
                    query.Update("redeemedTappables", playerId, redeemedTappables);
                    query.Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.TappableEntry(requestStartedOn, rewards.ToDBRewardsModel())));
                    query.Then(rewards.ToRedeemQuery(playerId, requestStartedOn, staticData));
                    query.Then(results2 => new EarthDB.Query(false).Extra("success", true).Extra("rewards", rewards));

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            if ((bool)results.GetExtra("success"))
            {
                return EarthJson(new Dictionary<string, object?>()
                {
                    { "token", new Token(
                        Token.Type.TAPPABLE,
                        [],
                        ((Utils.Rewards) results.GetExtra("rewards")).ToApiResponse(),
                        Token.LifetimeE.PERSISTENT
                    ) },
                    { "updates", null }
                }, new EarthApiResponse.UpdatesResponse(results));
            }
            else
            {
                return TypedResults.BadRequest();
            }
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
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
        foreach (var (encounterId, tileId) in requestedIds)
        {
            encounterStates[encounterId] = new EncounterState(EncounterState.ActiveEncounterStateE.PRISTINE);
        }

        return EarthJson(encounterStates);
    }

    private sealed record TappableRequest(
        string Id,
        Coordinate PlayerCoordinate
    );
}
