using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using Rewards = ViennaDotNet.ApiServer.Types.Common.Rewards;

namespace ViennaDotNet.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/player/challenges")]
public class ChallengesController : ControllerBase
{
    private sealed record ChallengeRecord(
        string ReferenceId,
        string? ParentId,
        string GroupId,
        string Duration,
        string Type,
        string Category,
        Rarity? Rarity,
        int Order,
        string EndTimeUtc,
        string State,
        bool IsComplete,
        int PercentComplete,
        int CurrentCount,
        int TotalThreshold,
        string[] PrerequisiteIds,
        string PrerequisiteLogicalCondition,
        Rewards Rewards,
        object ClientProperties
    );

    [HttpGet]
    public IActionResult Get()
    {
        // TODO: this is currently just a stub required for the journal to load properly in the client

        string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, object>()
        {
            { "challenges", new Dictionary<string, object>()
            {
                // client requires two season challenges with these specific persona item reward UUIDs to exist in order for the journal to load, and no one has any idea why
                { "00000000-0000-0000-0000-000000000001", new ChallengeRecord(
                    "00000000-0000-0000-0000-000000000001",
                    null,
                    "00000000-0000-0000-0000-000000000001",
                    "Season",
                    "Regular",
                    "season_1",
                    null,
                    0,
                    TimeFormatter.FormatTime(U.CurrentTimeMillis() + 24 * 60 * 60 * 1000),
                    "Locked",
                    false,
                    0,
                    0,
                    1,
                    [],
                    "And",
                    new Rewards(null, null, null, [], [], [], ["230f5996-04b2-4f0e-83e5-4056c7f1d946"], []),
                    new object()
                ) },
                { "00000000-0000-0000-0000-000000000002", new ChallengeRecord(
                   "00000000-0000-0000-0000-000000000002",
                    null,
                    "00000000-0000-0000-0000-000000000001",
                    "Season",
                    "Regular",
                    "season_1",
                    null,
                    0,
                    TimeFormatter.FormatTime(U.CurrentTimeMillis() + 24 * 60 * 60 * 1000),
                    "Locked",
                    false,
                    0,
                    0,
                    1,
                    [],
                    "And",
                    new Rewards(null, null, null, [], [], [], ["d7725840-4376-44fc-9220-585f45775371"], []),
                    new object()
                ) }
            } },
            { "activeSeasonChallenge", "00000000-0000-0000-0000-000000000000" },
        }));
        return Content(resp, "application/json");
    }
}
