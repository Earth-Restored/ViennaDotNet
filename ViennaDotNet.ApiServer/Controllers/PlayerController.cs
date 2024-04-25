using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ViennaDotNet.ApiServer.Utils;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}")]
    public class PlayerController : ControllerBase
    {
        [HttpGet]
        [Route("boosts")]
        public IActionResult GetBoosts()
        {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, object>()
            {
                { "potions", new object[5] },
                { "miniFigs", new object[5] },
                { "miniFigRecords", new Dictionary<string, object>() },
                { "activeEffects", new object[0] },
                { "scenarioBoosts", new Dictionary<string, object>() },
                { "expiration", null },
                { "statusEffects", new Dictionary<string, object>() {
                        { "tappableInteractionRadius", null },
                        { "experiencePointRate", null },
                        { "itemExperiencePointRates", null },
                        { "attackDamageRate", null },
                        { "playerDefenseRate", null },
                        { "blockDamageRate", null },
                        { "maximumPlayerHealth", 20 },
                        { "craftingSpeed", null },
                        { "smeltingFuelIntensity", null },
                        { "foodHealthRate", null },
                    }
                }
            }));
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
            return Content(resp, "application/json");
        }
    }
}
