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

namespace ViennaDotNet.ApiServer.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}")]
    public class TappablesController : ControllerBase
    {
        private static TappablesManager tappablesManager => Program.tappablesManager;
        private static EarthDB earthDB => Program.DB;
        private static Catalog catalog => Program.Catalog;

        [Route("locations/{lat}/{lon}")]
        public IActionResult GetTappables(double lat, double lon)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            tappablesManager.notifyTileActive(playerId, lat, lon);

            TappablesManager.Tappable[] tappables = tappablesManager.getTappablesAround(lat, lon, 5.0f);    // TODO: radius

            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                    .Get("redeemedTappables", playerId, typeof(RedeemedTappables))
                    .Execute(earthDB);
                RedeemedTappables redeemedTappables = (RedeemedTappables)results.Get("redeemedTappables").Value;

                ActiveLocation[] activeLocations = tappables
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
                    ))
                    .ToArray();

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

        [HttpPost]
        [Route("tappables/{tileId}")]
        public async Task<IActionResult> RedeemTappable(string tileId)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            TappableRequest? tappableRequest = await Request.Body.AsJson<TappableRequest>();
            if (tappableRequest is null)
                return BadRequest();

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            TappablesManager.Tappable? tappable = tappablesManager.getTappableWithId(tappableRequest.id, tileId);
            if (tappable == null || tappable.spawnTime > requestStartedOn || tappable.spawnTime + tappable.validFor <= requestStartedOn) // TODO: check player location is in radius
                return BadRequest();

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
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
                        query.Then(rewards.toRedeemQuery(playerId, requestStartedOn, catalog));
                        query.Then(results2 => new EarthDB.Query(false).Extra("success", true).Extra("rewards", rewards));

                        return query;
                    })
                    .Execute(earthDB);

                if ((bool)results.getExtra("success"))
                {
                    string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object?>()
                    {
                        { "token", new Token(
                            Token.Type.TAPPABLE,
                            new (),
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

        record TappableRequest(
                string id,
                Coordinate playerCoordinate
            )
        {
        }
    }
}
