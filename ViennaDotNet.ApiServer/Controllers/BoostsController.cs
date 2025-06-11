using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Linq;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Types.Profile;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
public class BoostsController : ControllerBase
{
    private static EarthDB earthDB => Program.DB;
    private static Catalog catalog => Program.staticData.catalog;

    [HttpGet("boosts")]
    public async Task<IActionResult> GetBoosts(CancellationToken cancellation)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

        Boosts boosts;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("boosts", playerId, typeof(Boosts))
                .ExecuteAsync(earthDB, cancellation);
            boosts = (Boosts)results.Get("boosts").Value;
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        boosts.prune(requestStartedOn);

        Boosts.ActiveBoost[] activeBoosts = boosts.getAll();
        Array.Sort(activeBoosts, (a, b) => a.startTime.CompareTo(b.startTime));

        var potions = new Types.Boost.Boosts.Potion[5];
        for (int index = 0; index < potions.Length && index < activeBoosts.Length; index++)
        {
            Boosts.ActiveBoost activeBoost = activeBoosts[index];
            potions[index] = new(true, activeBoost.itemId, activeBoost.instanceId, TimeFormatter.FormatTime(activeBoost.startTime + activeBoost.duration));
        }

        long expiry = long.MaxValue;
        LinkedList<Types.Boost.Boosts.ActiveEffect> activeEffects = [];
        LinkedList<Types.Boost.Boosts.ScenarioBoost> triggeredOnDeathBoosts = [];
        foreach (Boosts.ActiveBoost activeBoost in activeBoosts)
        {
            long boostExpiry = activeBoost.startTime + activeBoost.duration;
            if (boostExpiry < expiry)
            {
                expiry = boostExpiry;
            }

            Catalog.ItemsCatalog.Item? item = catalog.itemsCatalog.getItem(activeBoost.itemId);
            if (item is null || item.boostInfo is null)
            {
                continue;
            }

            if (!item.boostInfo.triggeredOnDeath)
            {
                foreach (Catalog.ItemsCatalog.Item.BoostInfo.Effect effect in item.boostInfo.effects)
                {
                    if (effect.activation != Catalog.ItemsCatalog.Item.BoostInfo.Effect.Activation.TIMED)
                    {
                        Log.Warning($"Active boost {activeBoost.itemId} has effect with activation {effect.activation}");
                        continue;
                    }

                    long effectExpiry = activeBoost.startTime + effect.duration;

                    activeEffects.AddLast(new Types.Boost.Boosts.ActiveEffect(BoostUtils.boostEffectToApiResponse(effect), TimeFormatter.FormatTime(effectExpiry)));

                    if (effectExpiry < expiry)
                    {
                        expiry = effectExpiry;
                    }
                }
            }
            else
            {
                LinkedList<Effect> effects = [];
                foreach (Catalog.ItemsCatalog.Item.BoostInfo.Effect effect in item.boostInfo.effects)
                {
                    if (effect.activation != Catalog.ItemsCatalog.Item.BoostInfo.Effect.Activation.TRIGGERED)
                    {
                        Log.Warning($"Active boost {activeBoost.itemId} has effect with activation {effect.activation}");
                        continue;
                    }

                    effects.AddLast(BoostUtils.boostEffectToApiResponse(effect));
                }

                triggeredOnDeathBoosts.AddLast(new Types.Boost.Boosts.ScenarioBoost(true, activeBoost.instanceId, [.. effects], TimeFormatter.FormatTime(boostExpiry)));
            }
        }

        Dictionary<string, Types.Boost.Boosts.ScenarioBoost[]> scenarioBoosts = [];
        if (triggeredOnDeathBoosts.Count > 0)
        {
            scenarioBoosts["death"] = [.. triggeredOnDeathBoosts];
        }

        Types.Boost.Boosts boostsResponse = new Types.Boost.Boosts(
            potions,
            new Types.Boost.Boosts.MiniFig[5],
            [.. activeEffects],
            scenarioBoosts,
            new Types.Boost.Boosts.StatusEffects(null, null, null, null, null, null, null, null, null, null),    // TODO
            [],
            activeBoosts.Length > 0 ? TimeFormatter.FormatTime(expiry) : null
        );

        string resp = JsonConvert.SerializeObject(new EarthApiResponse(boostsResponse));
        return Content(resp, "application/json");
    }

    [HttpPost("boosts/potions/{itemId}/activate")]
    public async Task<IActionResult> ActivateBoost(string itemId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

        Catalog.ItemsCatalog.Item? item = catalog.itemsCatalog.getItem(itemId);

        if (item is null || item.boostInfo is null || item.boostInfo.type is not Catalog.ItemsCatalog.Item.BoostInfo.Type.POTION)
        {
            return BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("inventory", playerId, typeof(Inventory))
                .Get("boosts", playerId, typeof(Boosts))
                .Then(results1 =>
                {
                    Inventory inventory = (Inventory)results1.Get("inventory").Value;
                    Boosts boosts = (Boosts)results1.Get("boosts").Value;
                    boosts.prune(requestStartedOn);

                    if (!inventory.takeItems(itemId, 1))
                    {
                        return new EarthDB.Query(false);
                    }

                    if (boosts.getAll().Length >= 5)
                    {
                        return new EarthDB.Query(false);
                    }

                    return new EarthDB.Query(true)
                        .Update("inventory", playerId, inventory)
                        .Then(BoostUtils.activatePotion(playerId, itemId, requestStartedOn, catalog.itemsCatalog));
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(null, new EarthApiResponse.Updates(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }
    }

    [HttpDelete("boosts/{instanceId}")]
    public async Task<IActionResult> DeactivateBoost(string instanceId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("boosts", playerId, typeof(Boosts))
                .Then(results1 =>
                {
                    Boosts boosts = (Boosts)results1.Get("boosts").Value;
                    boosts.prune(requestStartedOn);

                    Boosts.ActiveBoost? activeBoost = boosts.get(instanceId);
                    if (activeBoost is null)
                    {
                        return new EarthDB.Query(false);
                    }

                    Catalog.ItemsCatalog.Item? item = catalog.itemsCatalog.getItem(activeBoost.itemId);
                    if (item is null || item.boostInfo is null || !item.boostInfo.canBeRemoved)
                    {
                        return new EarthDB.Query(false);
                    }

                    boosts.remove(instanceId);

                    return new EarthDB.Query(true)
                        .Update("boosts", playerId, boosts);
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(null, new EarthApiResponse.Updates(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }
    }
}
