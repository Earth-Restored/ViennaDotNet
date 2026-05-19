using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Diagnostics;
using System.Security.Claims;
using Solace.ApiServer.Exceptions;
using Solace.ApiServer.Utils;
using Solace.Common.Utils;
using Solace.DB;
using Solace.DB.Models.Player;
using Solace.StaticData;
using Effect = Solace.ApiServer.Types.Common.Effect;
using Microsoft.EntityFrameworkCore;
using Solace.DB.Utils;

namespace Solace.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
internal sealed class BoostsController : SolaceControllerBase
{
    private readonly EarthDbContext _earthDB;
    private readonly Catalog _catalog;

    public BoostsController(EarthDbContext earthDB, StaticData.StaticData staticData)
    {
        _earthDB = earthDB;
        _catalog = staticData.Catalog;
    }

    private sealed record ActiveBoostInfo(
        BoostsEF.ActiveBoost ActiveBoost,
        Catalog.ItemsCatalogR.Item.BoostInfoR BoostInfo
    );

    [HttpGet("boosts")]
    public async Task<Results<ContentHttpResult, BadRequest>> GetBoosts(CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId))
        {
            return TypedResults.BadRequest();
        }

        long requestStartedOn = HttpContext.GetTimestamp();

        // I know this is ugly, we're making changes to the database in response to a GET request, but if we don't then the client won't correctly update the player health bar in the UI

        var boosts = await _earthDB.Boosts
            .AsNoTracking()
            .FirstOrNewAsync(boosts => boosts.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        var profile = await _earthDB.Profiles
            .AsTracking()
            .FirstOrNewAsync(profile => profile.Id == accountId, cancellationToken: cancellationToken);

        var results = new EarthDbContext.Results(_earthDB);

        if (PruneBoostsAndUpdateProfile(boosts, profile, requestStartedOn, _catalog.ItemsCatalog))
        {
            await _earthDB.SaveChangesAsync(cancellationToken);
            results.Profile = profile.Version;
        }

        Types.Boost.Boosts.Potion?[] potions = [.. boosts.ActiveBoosts.Select(activeBoost =>
        {
            return activeBoost is null
                ? null
                : new Types.Boost.Boosts.Potion(true, activeBoost.ItemId, activeBoost.InstanceId, TimeFormatter.FormatTime(activeBoost.StartTime + activeBoost.Duration));
        })];

        Dictionary<string, ActiveBoostInfo> activeBoostsWithInfo = [];
        foreach (var activeBoost in boosts.ActiveBoosts)
        {
            if (activeBoost is null)
            {
                continue;
            }

            Catalog.ItemsCatalogR.Item? item = _catalog.ItemsCatalog.GetItem(activeBoost.ItemId);
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

        BoostUtils.StatModiferValues statModiferValues = BoostUtils.GetActiveStatModifiers(boosts, requestStartedOn, _catalog.ItemsCatalog);

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
    public async Task<Results<ContentHttpResult, BadRequest>> ActivateBoost(string itemId, CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId))
        {
            return TypedResults.BadRequest();
        }

        long requestStartedOn = HttpContext.GetTimestamp();

        Catalog.ItemsCatalogR.Item? item = _catalog.ItemsCatalog.GetItem(itemId);

        if (item is null || item.BoostInfo is null || item.BoostInfo.Type is not Catalog.ItemsCatalogR.Item.BoostInfoR.TypeE.POTION)
        {
            return TypedResults.BadRequest();
        }

        var inventory = await _earthDB.Inventories
           .AsTracking()
           .FirstOrNewAsync(inventory => inventory.Id == accountId, cancellationToken: cancellationToken);

        var boosts = await _earthDB.Boosts
          .AsTracking()
          .FirstOrNewAsync(boosts => boosts.Id == accountId, cancellationToken: cancellationToken);

        var profile = await _earthDB.Profiles
            .AsTracking()
            .FirstOrNewAsync(profile => profile.Id == accountId, cancellationToken: cancellationToken);

        bool profileChanged = false;

        if (PruneBoostsAndUpdateProfile(boosts, profile, requestStartedOn, _catalog.ItemsCatalog))
        {
            profileChanged = true;
        }

        if (!inventory.TakeItems(itemId, 1))
        {
            return EarthJson(null, null);
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
            return EarthJson(null, null);
        }

        if (extendExisting)
        {
            var existingBoost = boosts.ActiveBoosts[newIndex];
            Debug.Assert(existingBoost is not null);

            boosts.ActiveBoosts[newIndex] = new BoostsEF.ActiveBoost(existingBoost.InstanceId, existingBoost.ItemId, existingBoost.StartTime, existingBoost.Duration + item.BoostInfo.Duration);
        }
        else
        {
            boosts.ActiveBoosts[newIndex] = new BoostsEF.ActiveBoost(Guid.NewGuid().ToString(), itemId, requestStartedOn, item.BoostInfo.Duration);
            if (item.BoostInfo.Effects.Any(effect => effect.Type is Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.TypeE.HEALTH))
            {
                // TODO: determine if we should add new player health straight away
                profileChanged = true;
            }
        }

        await _earthDB.SaveChangesAsync(cancellationToken);

        var results = new EarthDbContext.Results(_earthDB);
        results.Inventory = inventory.Version;
        results.Boosts = boosts.Version;

        if (profileChanged)
        {
            results.Profile = profile.Version;
        }

        await ActivityLogUtils.AddEntryAsync(results, accountId, new ActivityLogEF.BoostActivatedEntry(requestStartedOn, itemId));

        return EarthJson(null, new EarthApiResponse.UpdatesResponse(results));
    }

    [HttpDelete("boosts/{instanceId}")]
    public async Task<Results<ContentHttpResult, BadRequest>> DeactivateBoost(string instanceId, CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId))
        {
            return TypedResults.BadRequest();
        }

        long requestStartedOn = HttpContext.GetTimestamp();

        var boosts = await _earthDB.Boosts
            .AsTracking()
            .FirstOrNewAsync(boosts => boosts.Id == accountId, cancellationToken: cancellationToken);

        var profile = await _earthDB.Profiles
            .AsTracking()
            .FirstOrNewAsync(profile => profile.Id == accountId, cancellationToken: cancellationToken);

        bool profileChanged = false;

        if (PruneBoostsAndUpdateProfile(boosts, profile, requestStartedOn, _catalog.ItemsCatalog))
        {
            profileChanged = true;
        }

        var activeBoost = boosts.Get(instanceId);
        if (activeBoost is null)
        {
            return EarthJson(null, null);
        }

        var item = _catalog.ItemsCatalog.GetItem(activeBoost.ItemId);
        if (item is null || item.BoostInfo is null || !item.BoostInfo.CanBeRemoved)
        {
            return EarthJson(null, null);
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
            int maxPlayerHealth = BoostUtils.GetMaxPlayerHealth(boosts, requestStartedOn, _catalog.ItemsCatalog);
            if (profile.Health > maxPlayerHealth)
            {
                profile.Health = maxPlayerHealth;
            }
        }

        await _earthDB.SaveChangesAsync(cancellationToken);

        var results = new EarthDbContext.Results(_earthDB);
        results.Boosts = boosts.Version;

        if (profileChanged)
        {
            results.Profile = profile.Version;
        }

        return EarthJson(null, new EarthApiResponse.UpdatesResponse(results));
    }

    private static bool PruneBoostsAndUpdateProfile(BoostsEF boosts, ProfileEF profile, long currentTime, Catalog.ItemsCatalogR itemsCatalog)
    {
        bool profileChanged = false;
        BoostsEF.ActiveBoost[] prunedBoosts = boosts.Prune(currentTime);
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
