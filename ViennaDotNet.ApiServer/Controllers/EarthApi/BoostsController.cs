using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Diagnostics;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;
using Effect = ViennaDotNet.ApiServer.Types.Common.Effect;

namespace ViennaDotNet.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
public class BoostsController : ViennaControllerBase
{
    private static EarthDB earthDB => Program.DB;
    private static Catalog catalog => Program.staticData.Catalog;

    private sealed record ActiveBoostInfo(
        Boosts.ActiveBoost ActiveBoost,
        Catalog.ItemsCatalogR.Item.BoostInfoR BoostInfo
    );

    [HttpGet("boosts")]
    public async Task<IActionResult> GetBoosts(CancellationToken cancellation)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        long requestStartedOn = HttpContext.GetTimestamp();

        EarthDB.Results results;
        try
        {
            results = await new EarthDB.Query(true)
                .Get("boosts", playerId, typeof(Boosts))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    // I know this is ugly, we're making changes to the database in response to a GET request, but if we don't then the client won't correctly update the player health bar in the UI

                    Boosts boosts = results1.Get<Boosts>("boosts");
                    Profile profile = results1.Get<Profile>("profile");

                    return PruneBoostsAndUpdateProfile(boosts, profile, requestStartedOn, catalog.ItemsCatalog)
                        ? new EarthDB.Query(true)
                            .Update("boosts", playerId, boosts)
                            .Update("profile", playerId, profile)
                            .Extra("boosts", boosts)
                        : new EarthDB.Query(false)
                            .Extra("boosts", boosts);
                })
                .ExecuteAsync(earthDB, cancellation);
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        var boosts = (Boosts)results.GetExtra("boosts");

        Types.Boost.Boosts.Potion?[] potions = [.. boosts.ActiveBoosts.Select(activeBoost =>
        {
            return activeBoost is null
                ? null
                : new Types.Boost.Boosts.Potion(true, activeBoost.ItemId, activeBoost.InstanceId, TimeFormatter.FormatTime(activeBoost.StartTime + activeBoost.Duration));
        })];

        Dictionary<string, ActiveBoostInfo> activeBoostsWithInfo = [];
        foreach (Boosts.ActiveBoost? activeBoost in boosts.ActiveBoosts)
        {
            if (activeBoost is null)
            {
                continue;
            }

            Catalog.ItemsCatalogR.Item? item = catalog.ItemsCatalog.GetItem(activeBoost.ItemId);
            if (item is null || item.BoostInfo is null)
            {
                continue;
            }

            ActiveBoostInfo? existingActiveBoostInfo = activeBoostsWithInfo.GetValueOrDefault(item.BoostInfo.Name);
            if (existingActiveBoostInfo is not null && existingActiveBoostInfo.BoostInfo.Level > item.BoostInfo.Level)
            {
                continue;
            }

            activeBoostsWithInfo[item.BoostInfo.Name] = new ActiveBoostInfo(activeBoost, item.BoostInfo);
        }

        LinkedList<Types.Boost.Boosts.ActiveEffect> activeEffects = [];
        LinkedList<Types.Boost.Boosts.ScenarioBoost> triggeredOnDeathBoosts = [];
        foreach (ActiveBoostInfo activeBoostInfo in activeBoostsWithInfo.Values)
        {
            if (!activeBoostInfo.BoostInfo.TriggeredOnDeath)
            {
                foreach (Catalog.ItemsCatalogR.Item.BoostInfoR.Effect effect in activeBoostInfo.BoostInfo.Effects)
                {
                    if (effect.Activation != Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.ActivationE.TIMED)
                    {
                        Log.Warning($"Active boost {activeBoostInfo.ActiveBoost.ItemId} has effect with activation {effect.Activation}");
                        continue;
                    }

                    activeEffects.AddLast(new Types.Boost.Boosts.ActiveEffect(BoostUtils.BoostEffectToApiResponse(effect, activeBoostInfo.ActiveBoost.Duration), TimeFormatter.FormatTime(activeBoostInfo.ActiveBoost.StartTime + activeBoostInfo.ActiveBoost.Duration)));
                }
            }
            else
            {
                LinkedList<Effect> effects = [];
                foreach (Catalog.ItemsCatalogR.Item.BoostInfoR.Effect effect in activeBoostInfo.BoostInfo.Effects)
                {
                    if (effect.Activation != Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.ActivationE.TRIGGERED)
                    {
                        Log.Warning($"Active boost {activeBoostInfo.ActiveBoost.ItemId} has effect with activation {effect.Activation}");
                        continue;
                    }

                    effects.AddLast(BoostUtils.BoostEffectToApiResponse(effect, activeBoostInfo.ActiveBoost.Duration));
                }

                triggeredOnDeathBoosts.AddLast(new Types.Boost.Boosts.ScenarioBoost(true, activeBoostInfo.ActiveBoost.InstanceId, [.. effects], TimeFormatter.FormatTime(activeBoostInfo.ActiveBoost.StartTime + activeBoostInfo.ActiveBoost.Duration)));
            }
        }

        Dictionary<string, Types.Boost.Boosts.ScenarioBoost[]> scenarioBoosts = [];
        if (triggeredOnDeathBoosts.Count > 0)
        {
            scenarioBoosts["death"] = [.. triggeredOnDeathBoosts];
        }

        BoostUtils.StatModiferValues statModiferValues = BoostUtils.GetActiveStatModifiers(boosts, requestStartedOn, catalog.ItemsCatalog);

        var boostsResponse = new Types.Boost.Boosts(
            potions,
            new Types.Boost.Boosts.MiniFig[5],
            [.. activeEffects],
            scenarioBoosts,
            new Types.Boost.Boosts.StatusEffectsR(
                statModiferValues.TappableInteractionRadiusExtraMeters > 0 ? statModiferValues.TappableInteractionRadiusExtraMeters + 70 : null,
                null,
                null,
                statModiferValues.AttackMultiplier > 0 ? statModiferValues.AttackMultiplier + 100 : null,
                statModiferValues.DefenseMultiplier > 0 ? statModiferValues.DefenseMultiplier + 100 : null,
                statModiferValues.MiningSpeedMultiplier > 0 ? statModiferValues.MiningSpeedMultiplier + 100 : null,
                statModiferValues.MaxPlayerHealthMultiplier > 0 ? 20 * statModiferValues.MaxPlayerHealthMultiplier / 100 + 20 : 20,
                statModiferValues.CraftingSpeedMultiplier > 0 ? statModiferValues.CraftingSpeedMultiplier / 100 + 1 : null,
                statModiferValues.SmeltingSpeedMultiplier > 0 ? statModiferValues.SmeltingSpeedMultiplier / 100 + 1 : null,
                statModiferValues.FoodMultiplier > 0 ? (statModiferValues.FoodMultiplier + 100) / 100f : null
            ),
            [],
            activeBoostsWithInfo.Count != 0 ? TimeFormatter.FormatTime(activeBoostsWithInfo.Values.Select(activeBoostInfo => activeBoostInfo.ActiveBoost.StartTime + activeBoostInfo.ActiveBoost.Duration).Min()) : null
        );

        return EarthJson(boostsResponse, new EarthApiResponse.UpdatesResponse(results));
    }

    [HttpPost("boosts/potions/{itemId}/activate")]
    public async Task<IActionResult> ActivateBoost(string itemId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        long requestStartedOn = HttpContext.GetTimestamp();

        Catalog.ItemsCatalogR.Item? item = catalog.ItemsCatalog.GetItem(itemId);

        if (item is null || item.BoostInfo is null || item.BoostInfo.Type is not Catalog.ItemsCatalogR.Item.BoostInfoR.TypeE.POTION)
        {
            return BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("inventory", playerId, typeof(Inventory))
                .Get("boosts", playerId, typeof(Boosts))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    Inventory inventory = results1.Get<Inventory>("inventory");
                    Boosts boosts = results1.Get<Boosts>("boosts");
                    Profile profile = results1.Get<Profile>("profile");
                    bool profileChanged = false;

                    if (PruneBoostsAndUpdateProfile(boosts, profile, requestStartedOn, catalog.ItemsCatalog))
                    {
                        profileChanged = true;
                    }

                    if (!inventory.TakeItems(itemId, 1))
                    {
                        return new EarthDB.Query(false);
                    }

                    int newIndex = -1;
                    bool extendExisting = false;
                    for (int index = 0; index < boosts.ActiveBoosts.Length; index++)
                    {
                        var boost = boosts.ActiveBoosts[index];

                        if (boost is not null && boost.ItemId == itemId)
                        {
                            newIndex = index;
                            break;
                        }
                    }

                    if (!extendExisting)
                    {
                        for (int index = 0; index < boosts.ActiveBoosts.Length; index++)
                        {
                            if (boosts.ActiveBoosts[index] is null)
                            {
                                newIndex = index;
                                break;
                            }
                        }
                    }

                    if (newIndex == -1)
                    {
                        return new EarthDB.Query(false);
                    }

                    if (extendExisting)
                    {
                        Boosts.ActiveBoost? existingBoost = boosts.ActiveBoosts[newIndex];
                        Debug.Assert(existingBoost is not null);

                        boosts.ActiveBoosts[newIndex] = new Boosts.ActiveBoost(existingBoost.InstanceId, existingBoost.ItemId, existingBoost.StartTime, existingBoost.Duration + item.BoostInfo.Duration);
                    }
                    else
                    {
                        boosts.ActiveBoosts[newIndex] = new Boosts.ActiveBoost(U.RandomUuid().ToString(), itemId, requestStartedOn, item.BoostInfo.Duration);
                        if (item.BoostInfo.Effects.Any(effect => effect.Type is Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.TypeE.HEALTH))
                        {
                            // TODO: determine if we should add new player health straight away
                            profileChanged = true;
                        }
                    }

                    var updateQuery = new EarthDB.Query(true);
                    updateQuery.Update("inventory", playerId, inventory);
                    updateQuery.Update("boosts", playerId, boosts);

                    if (profileChanged)
                    {
                        updateQuery.Update("profile", playerId, profile);
                    }

                    updateQuery.Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.BoostActivatedEntry(requestStartedOn, itemId)));
                    return updateQuery;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            return EarthJson(null, new EarthApiResponse.UpdatesResponse(results));
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

        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("boosts", playerId, typeof(Boosts))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    Boosts boosts = results1.Get<Boosts>("boosts");
                    Profile profile = results1.Get<Profile>("profile");
                    bool profileChanged = false;

                    if (PruneBoostsAndUpdateProfile(boosts, profile, requestStartedOn, catalog.ItemsCatalog))
                    {
                        profileChanged = true;
                    }

                    Boosts.ActiveBoost? activeBoost = boosts.Get(instanceId);
                    if (activeBoost is null)
                    {
                        return new EarthDB.Query(false);
                    }

                    Catalog.ItemsCatalogR.Item? item = catalog.ItemsCatalog.GetItem(activeBoost.ItemId);
                    if (item is null || item.BoostInfo is null || !item.BoostInfo.CanBeRemoved)
                    {
                        return new EarthDB.Query(false);
                    }

                    for (int index = 0; index < boosts.ActiveBoosts.Length; index++)
                    {
                        var boost = boosts.ActiveBoosts[index];

                        if (boost is not null && boost.InstanceId == instanceId)
                        {
                            boosts.ActiveBoosts[index] = null;
                        }
                    }

                    if (item.BoostInfo.Effects.Any(effect => effect.Type is Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.TypeE.HEALTH))
                    {
                        profileChanged = true;
                        int maxPlayerHealth = BoostUtils.GetMaxPlayerHealth(boosts, requestStartedOn, catalog.ItemsCatalog);
                        if (profile.Health > maxPlayerHealth)
                        {
                            profile.Health = maxPlayerHealth;
                        }
                    }

                    var updateQuery = new EarthDB.Query(true);
                    updateQuery.Update("boosts", playerId, boosts);
                    if (profileChanged)
                    {
                        updateQuery.Update("profile", playerId, profile);
                    }

                    return updateQuery;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            return EarthJson(null, new EarthApiResponse.UpdatesResponse(results));
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }
    }

    private static bool PruneBoostsAndUpdateProfile(Boosts boosts, Profile profile, long currentTime, Catalog.ItemsCatalogR itemsCatalog)
    {
        bool profileChanged = false;
        Boosts.ActiveBoost[] prunedBoosts = boosts.Prune(currentTime);
        if (prunedBoosts.SelectMany(activeBoost => itemsCatalog.GetItem(activeBoost.ItemId)!.BoostInfo!.Effects).Any(effect => effect.Type is Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.TypeE.HEALTH))
        {
            profileChanged = true;
        }

        int maxPlayerHealth = BoostUtils.GetMaxPlayerHealth(boosts, currentTime, itemsCatalog);
        if (profile.Health > maxPlayerHealth)
        {
            profile.Health = maxPlayerHealth;
            profileChanged = true;
        }

        return profileChanged;
    }
}
