using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace ViennaDotNet.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}")]
    public class EnvironmentSettingsController : ControllerBase
    {
        [Route("/features")]
        public IActionResult Features()
        {
            JObject resp = new JObject(
                new JProperty("workshop_enabled", true),
                new JProperty("buildplates_enabled", true),
                new JProperty("enable_ruby_purchasing", true),
                new JProperty("commerce_enabled", true),
                new JProperty("full_logging_enabled", true),
                new JProperty("challenges_enabled", true),
                new JProperty("craftingv2_enabled", true),
                new JProperty("smeltingv2_enabled", true),
                new JProperty("inventory_item_boosts_enabled", true),
                new JProperty("player_health_enabled", true),
                new JProperty("minifigs_enabled", true),
                new JProperty("potions_enabled", true),
                new JProperty("social_link_launch_enabled", true),
                new JProperty("social_link_share_enabled", true),
                new JProperty("encoded_join_enabled", true),
                new JProperty("adventure_crystals_enabled", true),
                new JProperty("item_limits_enabled", true),
                new JProperty("adventure_crystals_ftue_enabled", true),
                new JProperty("expire_crystals_on_cleanup_enabled", true),
                new JProperty("challenges_v2_enabled", true),
                new JProperty("player_journal_enabled", true),
                new JProperty("player_stats_enabled", true),
                new JProperty("activity_log_enabled", true),
                new JProperty("seasons_enabled", true),
                new JProperty("daily_login_enabled", true),
                new JProperty("store_pdp_enabled", true),
                new JProperty("hotbar_stacksplitting_enabled", true),
                new JProperty("fancy_rewards_screen_enabled", true),
                new JProperty("async_ecs_dispatcher", true),
                new JProperty("adventure_oobe_enabled", true),
                new JProperty("tappable_oobe_enabled", true),
                new JProperty("map_permission_oobe_enabled", true),
                new JProperty("journal_oobe_enabled", true),
                new JProperty("freedom_oobe_enabled", true),
                new JProperty("challenge_oobe_enabled", true),
                new JProperty("level_rewards_v2_enabled", true),
                new JProperty("content_driven_season_assets", true),
                new JProperty("paid_earned_rubies_enabled", true)
            );

            return Content(JsonConvert.SerializeObject(resp, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), "application/json");
        }

        [Route("/settings")]
        public IActionResult Settings()
        {
            JObject resp = new JObject(
                new JProperty("encounterinteractionradius", 40),
                new JProperty("tappableinteractionradius", 70),
                new JProperty("tappablevisibleradius", -5),
                new JProperty("targetpossibletappables", 100),
                new JProperty("tile0", 10537),
                new JProperty("slowrequesttimeout", 2500),
                new JProperty("cullingradius", 50),
                new JProperty("commontapcount", 3),
                new JProperty("epictapcount", 7),
                new JProperty("speedwarningcooldown", 3600),
                new JProperty("mintappablesrequiredpertile", 22),
                new JProperty("targetactivetappables", 30),
                new JProperty("tappablecullingradius", 500),
                new JProperty("raretapcount", 5),
                new JProperty("requestwarningtimeout", 10000),
                new JProperty("speedwarningthreshold", 11.176f),
                new JProperty("asaanchormaxplaneheightthreshold", 0.5f),
                new JProperty("maxannouncementscount", 0),
                new JProperty("removethislater", 23),
                new JProperty("crystalslotcap", 3),
                new JProperty("crystaluncommonduration", 10),
                new JProperty("crystalrareduration", 10),
                new JProperty("crystalepicduration", 10),
                new JProperty("crystalcommonduration", 10),
                new JProperty("crystallegendaryduration", 10),
                new JProperty("maximumpersonaltimedchallenges", 3),
                new JProperty("maximumpersonalcontinuouschallenges", 3)
            );

            return Content(JsonConvert.SerializeObject(resp, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), "application/json");
        }
    }
}
