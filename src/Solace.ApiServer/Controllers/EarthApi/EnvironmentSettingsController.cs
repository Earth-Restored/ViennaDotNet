using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Solace.ApiServer.Utils;
using Solace.Common;

namespace Solace.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
internal sealed class EnvironmentSettingsController : ControllerBase
{
    [HttpGet("features")]
    public ContentHttpResult Features()
    {
        var resp = new EarthApiResponse(new Dictionary<string, object>
        {
            ["workshop_enabled"] = true,
            ["buildplates_enabled"] = true,
            ["enable_ruby_purchasing"] = true,
            ["commerce_enabled"] = true,
            ["full_logging_enabled"] = true,
            ["challenges_enabled"] = true,
            ["craftingv2_enabled"] = true,
            ["smeltingv2_enabled"] = true,
            ["inventory_item_boosts_enabled"] = true,
            ["player_health_enabled"] = true,
            ["minifigs_enabled"] = true,
            ["potions_enabled"] = true,
            ["social_link_launch_enabled"] = true,
            ["social_link_share_enabled"] = true,
            ["encoded_join_enabled"] = true,
            ["adventure_crystals_enabled"] = true,
            ["item_limits_enabled"] = true,
            ["adventure_crystals_ftue_enabled"] = true,
            ["expire_crystals_on_cleanup_enabled"] = true,
            ["challenges_v2_enabled"] = true,
            ["player_journal_enabled"] = true,
            ["player_stats_enabled"] = true,
            ["activity_log_enabled"] = true,
            ["seasons_enabled"] = false,
            ["daily_login_enabled"] = true,
            ["store_pdp_enabled"] = true,
            ["hotbar_stacksplitting_enabled"] = true,
            ["fancy_rewards_screen_enabled"] = true,
            ["async_ecs_dispatcher"] = true,
            ["adventure_oobe_enabled"] = true,
            ["tappable_oobe_enabled"] = true,
            ["map_permission_oobe_enabled"] = true,
            ["journal_oobe_enabled"] = true,
            ["freedom_oobe_enabled"] = true,
            ["challenge_oobe_enabled"] = true,
            ["level_rewards_v2_enabled"] = true,
            ["content_driven_season_assets"] = true,
            ["paid_earned_rubies_enabled"] = true,
        });

        string sResp = Json.Serialize(resp, new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        return TypedResults.Content(sResp, "application/json");
    }

    [HttpGet("settings")]
    public ContentHttpResult Settings()
    {
        var resp = new EarthApiResponse(new Dictionary<string, object>
        {
            ["encounterinteractionradius"] = 40,
            ["tappableinteractionradius"] = 70,
            ["tappablevisibleradius"] = -5,
            ["targetpossibletappables"] = 100,
            ["tile0"] = 10537,
            ["slowrequesttimeout"] = 2500,
            ["cullingradius"] = 50,
            ["commontapcount"] = 3,
            ["epictapcount"] = 7,
            ["speedwarningcooldown"] = 3600,
            ["mintappablesrequiredpertile"] = 22,
            ["targetactivetappables"] = 30,
            ["tappablecullingradius"] = 500,
            ["raretapcount"] = 5,
            ["requestwarningtimeout"] = 10000,
            ["speedwarningthreshold"] = 11.176f,
            ["asaanchormaxplaneheightthreshold"] = 0.5f,
            ["maxannouncementscount"] = 0,
            ["removethislater"] = 23,
            ["crystalslotcap"] = 3,
            ["crystaluncommonduration"] = 10,
            ["crystalrareduration"] = 10,
            ["crystalepicduration"] = 10,
            ["crystalcommonduration"] = 10,
            ["crystallegendaryduration"] = 10,
            ["maximumpersonaltimedchallenges"] = 3,
            ["maximumpersonalcontinuouschallenges"] = 3
        });

        string sResp = Json.Serialize(resp, new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        return TypedResults.Content(sResp, "application/json");
    }
}
