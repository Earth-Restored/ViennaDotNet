using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Types.Tappables;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
public class TappablesController : ControllerBase
{
    private static TappablesManager tappablesManager => Program.tappablesManager;
    private static EarthDB earthDB => Program.DB;
    private static StaticData.StaticData staticData => Program.staticData;

    [HttpGet("locations/{lat}/{lon}")]
    public async Task<IActionResult> GetTappables(double lat, double lon, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
            return BadRequest();

        long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

        tappablesManager.notifyTileActive(playerId, lat, lon);

        TappablesManager.Tappable[] tappables = tappablesManager.getTappablesAround(lat, lon, 5.0);    // TODO: radius
        TappablesManager.Encounter[] encounters = tappablesManager.getEncountersAround(lat, lon, 5.0);    // TODO: radius

        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("redeemedTappables", playerId, typeof(RedeemedTappables))
                .ExecuteAsync(earthDB, cancellationToken);
            RedeemedTappables redeemedTappables = (RedeemedTappables)results.Get("redeemedTappables").Value;

            IEnumerable<ActiveLocation> activeLocationTappables = tappables
                .Where(tappable => tappable.spawnTime + tappable.validFor > requestStartedOn && !redeemedTappables.isRedeemed(tappable.id))
                .Select(tappable => new ActiveLocation(
                    tappable.id,
                    TappablesManager.locationToTileId(tappable.lat, tappable.lon),
                    new Coordinate(tappable.lat, tappable.lon),
                    TimeFormatter.FormatTime(tappable.spawnTime),
                    TimeFormatter.FormatTime(tappable.spawnTime + tappable.validFor),
                    ActiveLocation.Type.TAPPABLE,
                    tappable.icon,
                    new ActiveLocation.Metadata(U.RandomUuid().ToString(), Enum.Parse<Rarity>(tappable.rarity.ToString())),
                    new ActiveLocation.TappableMetadata(Enum.Parse<Rarity>(tappable.rarity.ToString())),
                    null
                ));

            IEnumerable<ActiveLocation> activeLocationEncounters = encounters
                .Where(encounter => encounter.spawnTime + encounter.validFor > requestStartedOn)
                .Select(encounter => new ActiveLocation(
                    encounter.id,
                    TappablesManager.locationToTileId(encounter.lat, encounter.lon),
                    new Coordinate(encounter.lat, encounter.lon),
                    TimeFormatter.FormatTime(encounter.spawnTime),
                    TimeFormatter.FormatTime(encounter.spawnTime + encounter.validFor),
                    ActiveLocation.Type.ENCOUNTER,
                    encounter.icon,
                    new ActiveLocation.Metadata(U.RandomUuid().ToString(), Enum.Parse<Rarity>(encounter.rarity.ToString())),
                    null,
                    new ActiveLocation.EncounterMetadata(
                        ActiveLocation.EncounterMetadata.EncounterType.SHORT_4X4_PEACEFUL,    // TODO
                                                                                              //UUID.randomUUID().toString(),    // TODO: what is this field for and does it matter what we put here?
                        encounter.id,
                        encounter.encounterBuildplateId,
                        ActiveLocation.EncounterMetadata.AnchorState.OFF,
                        "",
                        ""
                    )
                ));

            ActiveLocation[] activeLocations = [.. activeLocationTappables, .. activeLocationEncounters];

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object>()
            {
                { "killSwitchedTileIds", new List<object>() },
                { "activeLocations", activeLocations }
            }));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("tappables/{tileId}")]
    public async Task<IActionResult> RedeemTappable(string tileId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
            return BadRequest();

        TappableRequest? tappableRequest = await Request.Body.AsJsonAsync<TappableRequest>(cancellationToken);
        if (tappableRequest is null)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

        TappablesManager.Tappable? tappable = tappablesManager.getTappableWithId(tappableRequest.id, tileId);
        if (tappable == null || !tappablesManager.isTappableValidFor(tappable, requestStartedOn, tappableRequest.playerCoordinate.latitude, tappableRequest.playerCoordinate.longitude))
        {
            return BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("redeemedTappables", playerId, typeof(RedeemedTappables))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);

                    RedeemedTappables redeemedTappables = (RedeemedTappables)results1.Get("redeemedTappables").Value;

                    if (redeemedTappables.isRedeemed(tappable.id))
                    {
                        query.Extra("success", false);
                        return query;
                    }

                    var rewards = new Utils.Rewards();
                    rewards.addExperiencePoints(tappable.drops.experiencePoints);
                    foreach (TappablesManager.Tappable.Drops.Item item in tappable.drops.items)
                        rewards.addItem(item.id, item.count);

                    rewards.addRubies(1);

                    redeemedTappables.add(tappable.id, tappable.spawnTime + tappable.validFor);
                    redeemedTappables.prune(requestStartedOn);
                    query.Update("redeemedTappables", playerId, redeemedTappables);
                    query.Then(ActivityLogUtils.addEntry(playerId, new ActivityLog.TappableEntry(requestStartedOn, rewards.toDBRewardsModel())));
                    query.Then(rewards.toRedeemQuery(playerId, requestStartedOn, staticData));
                    query.Then(results2 => new EarthDB.Query(false).Extra("success", true).Extra("rewards", rewards));

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            if ((bool)results.getExtra("success"))
            {
                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object?>()
                {
                    { "token", new Token(
                        Token.Type.TAPPABLE,
                        [],
                        ((Utils.Rewards) results.getExtra("rewards")).toApiResponse(),
                        Token.Lifetime.PERSISTENT
                    ) },
                    { "updates", null }
                }, new EarthApiResponse.Updates(results)));
                return Content(resp, "application/json");
            }
            else
                return BadRequest();
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("multiplayer/encounters/state")]
    public async Task<IActionResult> EncountersState(CancellationToken cancellationToken)
    {
        var requestedIds = await Request.Body.AsJsonAsync<Dictionary<string, object>>(cancellationToken);

        if (requestedIds is null)
        {
            return BadRequest();
        }

        foreach (var entry in requestedIds)
        {
            if (entry.Value is not string)
            {
                return BadRequest();
            }
        }

        // TODO

        var encounterStates = new Dictionary<string, EncounterState>();
        foreach (var (encounterId, tileId) in requestedIds)
        {
            encounterStates[encounterId] = new EncounterState(EncounterState.ActiveEncounterState.PRISTINE);
        }

        string resp = JsonConvert.SerializeObject(new EarthApiResponse(encounterStates));
        return Content(resp, "application/json");
    }

    private sealed record TappableRequest(
        string id,
        Coordinate playerCoordinate
    );
}
