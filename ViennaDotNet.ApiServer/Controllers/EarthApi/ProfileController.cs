using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/player")]
public class ProfileController : ControllerBase
{
    private static EarthDB earthDB => Program.DB;
    private static StaticData.StaticData staticData => Program.staticData;

    [HttpGet("profile/{userId}")]
    public async Task<IActionResult> GetProfile(string userId, CancellationToken cancellationToken)
    {
        // TODO: decide if we should allow requests for profiles of other players
        userId = userId.ToLowerInvariant();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        EarthDB.Results results = await new EarthDB.Query(false)
            .Get("profile", userId, typeof(Profile))
            .Get("boosts", userId, typeof(Boosts))
            .ExecuteAsync(earthDB, cancellationToken);

        Profile profile = results.Get<Profile>("profile");
        Boosts boosts = results.Get<Boosts>("boosts");

        var levels = staticData.Levels.Levels;
        int currentLevelExperience = profile.Experience - (profile.Level > 1 ? profile.Level - 2 < levels.Length ? levels[profile.Level - 2].ExperienceRequired : levels[^1].ExperienceRequired : 0);
        int experienceRemaining = profile.Level - 1 < levels.Length ? levels[profile.Level - 1].ExperienceRequired - profile.Experience : 0;

        int maxPlayerHealth = BoostUtils.GetMaxPlayerHealth(boosts, requestStartedOn, staticData.Catalog.ItemsCatalog);
        if (profile.Health > maxPlayerHealth)
        {
            profile.Health = maxPlayerHealth;
        }

        string resp = Json.Serialize(new EarthApiResponse(new Types.Profile.Profile(
            Java.IntStream.Range(0, levels.Length).Collect(() => new Dictionary<int, Types.Profile.Profile.LevelR>(), (hashMap, levelIndex) =>
            {
                PlayerLevels.Level level = levels[levelIndex];
                hashMap[levelIndex + 1] = new Types.Profile.Profile.LevelR(level.ExperienceRequired, LevelUtils.MakeLevelRewards(level).ToApiResponse());
            }, DictionaryExtensions.AddRange),
            profile.Experience,
            profile.Level,
            currentLevelExperience,
            experienceRemaining,
            profile.Health,
            profile.Health / (float)maxPlayerHealth * 100.0f)));

        return Content(resp, "application/json");
    }

    [ResponseCache(Duration = 11200)]
    [HttpGet("rubies")]
    public async Task<IActionResult> GetRubies(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        try
        {
            Profile profile = (await new EarthDB.Query(false)
                .Get("profile", playerId, typeof(Profile))
                .ExecuteAsync(earthDB, cancellationToken))
                .Get<Profile>("profile");

            string resp = Json.Serialize(new EarthApiResponse(profile.Rubies.Purchased + profile.Rubies.Earned));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            Log.Error("Exception in GetRubies", ex);
            return StatusCode(500);
        }
    }

    [ResponseCache(Duration = 11200)]
    [HttpGet("splitRubies")]
    public async Task<IActionResult> GetSplitRubies(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
            return BadRequest();

        try
        {
            Profile profile = (await new EarthDB.Query(false)
                .Get("profile", playerId, typeof(Profile))
                .ExecuteAsync(earthDB, cancellationToken))
                .Get<Profile>("profile");

            string resp = Json.Serialize(new EarthApiResponse(new Types.Profile.SplitRubies(profile.Rubies.Purchased, profile.Rubies.Earned)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            Log.Error("Exception in GetRubies", ex);
            return StatusCode(500);
        }
    }

    // required for the language selection option in the client to work
    [HttpPost("profile/language")]
    public IActionResult ChangeLanguage()
        => Ok();
}
