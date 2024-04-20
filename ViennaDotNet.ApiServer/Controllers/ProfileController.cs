using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.DB;
using DatabaseException = ViennaDotNet.DB.EarthDB.DatabaseException;
using Newtonsoft.Json;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}/player")]
    public class ProfileController : ControllerBase
    {
        private static EarthDB earthDB => Program.DB;

        [Route("profile/{profileID}")]
        public IActionResult GetProfile()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            Profile profile = (Profile)new EarthDB.Query(false)
                    .Get("profile", playerId, typeof(Profile))
                    .Execute(earthDB)
                    .Get("profile").Value;

            LevelUtils.Level[] levels = LevelUtils.getLevels();
            int currentLevelExperience = profile.experience - (profile.level > 1 ? (profile.level - 2 < levels.Length ? levels[profile.level - 2].experienceRequired : levels[levels.Length - 1].experienceRequired) : 0);
            int experienceRemaining = profile.level - 1 < levels.Length ? levels[profile.level - 1].experienceRequired - profile.experience : 0;

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Types.Profile.Profile(
                Java.IntStream.Range(0, levels.Length).Collect(() => new Dictionary<int, Types.Profile.Profile.Level>(), (hashMap, levelIndex) =>
                {
                    LevelUtils.Level level = levels[levelIndex];
                    hashMap[levelIndex + 1] = new Types.Profile.Profile.Level(level.experienceRequired, level.rewards.toApiResponse());
                }, DictionaryExtensions.AddRange),
                profile.experience,
                profile.level,
                currentLevelExperience,
                experienceRemaining,
                profile.health,
                ((float)profile.health / 20.0f) * 100.0f)));
            return Content(resp, "application/json");
        }

        [ResponseCache(Duration = 11200)]
        [Route("rubies")]
        public IActionResult GetRubies()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            try
            {
                Profile profile = (Profile)new EarthDB.Query(false)
                    .Get("profile", playerId, typeof(Profile))
                    .Execute(earthDB)
                    .Get("profile").Value;

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(profile.rubies.purchased + profile.rubies.earned));
                return Content(resp, "application/json");
            }
            catch (DatabaseException ex)
            {
                Log.Error("Exception in GetRubies", ex);
                return StatusCode(500);
            }
        }

        [ResponseCache(Duration = 11200)]
        [Route("splitRubies")]
        public IActionResult GetSplitRubies()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            try
            {
                Profile profile = (Profile)new EarthDB.Query(false)
                    .Get("profile", playerId, typeof(Profile))
                    .Execute(earthDB)
                    .Get("profile").Value;

                string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Types.Profile.SplitRubies(profile.rubies.purchased, profile.rubies.earned)));
                return Content(resp, "application/json");
            }
            catch (DatabaseException ex)
            {
                Log.Error("Exception in GetRubies", ex);
                return StatusCode(500);
            }
        }
    }
}
