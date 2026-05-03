using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using Solace.ApiServer.Models;
using Solace.ApiServer.Models.Playfab;
using Solace.ApiServer.Utils;
using Solace.Common.Utils;

namespace Solace.ApiServer.Controllers.PlayfabApi;

[Route("Client")]
[Route("20CA2.playfabapi.com/Client")]
internal sealed partial class ClientController : SolaceControllerBase
{
    private static Config config => Program.config;

    private sealed record GetUserPublisherDataRequest(
        GetUserPublisherDataRequest.EntityR Entity,
        string[] Keys
    )
    {
        internal sealed record EntityR(
            string Id,
            string Type
        );
    }

    [HttpPost("GetUserPublisherData")]
    public async Task<Results<ContentHttpResult, ForbidHttpResult, BadRequest>> GetUserPublisherData()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<GetUserPublisherDataRequest>(cancellationToken);

        if (request is null)
        {
            return TypedResults.BadRequest();
        }

        if (!Request.Headers.TryGetValue("X-Authorization", out var tokenHeader) || tokenHeader.Count < 1)
        {
            return TypedResults.BadRequest();
        }

        Match tokenMatch = GetAuthRegex().Match(tokenHeader[0] ?? "");

        string? tokenString = tokenMatch.Success ? tokenMatch.Groups[1].Value : null;

        if (tokenString is null)
        {
            return TypedResults.BadRequest();
        }

        var token = JwtUtils.Verify<Tokens.Shared.PlayfabSessionTicket>(tokenString, config.PlayfabApi.SessionTicketSecretBytes);
        if (token is null)
        {
            return TypedResults.Forbid();
        }

        switch (request.Entity.Type)
        {
            case "master_player_account":
                {
                    var publisherData = new Dictionary<string, object>()
                    {
                        ["PlayFabCommerceEnabled"] = new Dictionary<string, string>()
                        {
                            ["Value"] = "true",
                            ["LastUpdated"] = "2019-12-01T00:00:00Z",
                            ["Permission"] = "Public",
                        },
                        ["DataVersion"] = 35,
                    };

                    return JsonPascalCase(new PlayfabOkResponse(
                        200,
                        "OK",
                        new Dictionary<string, object>()
                        {
                            ["Data"] = request.Keys
                                .Where(publisherData.ContainsKey)
                                .ToDictionary(field => field, field => publisherData[field]),
                            ["DataVersion"] = 35,
                        }
                    ));
                }

            default:
                return TypedResults.BadRequest();
        }
    }

    private sealed record GetPlayerStatisticsRequest(
        string[] StatisticNames
    );

    [HttpPost("GetPlayerStatistics")]
    public async Task<Results<ContentHttpResult, ForbidHttpResult, BadRequest>> GetPlayerStatistics()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<GetPlayerStatisticsRequest>(cancellationToken);

        if (request is null)
        {
            return TypedResults.BadRequest();
        }

        if (!Request.Headers.TryGetValue("X-Authorization", out var tokenHeader) || tokenHeader.Count < 1)
        {
            return TypedResults.BadRequest();
        }

        Match tokenMatch = GetAuthRegex().Match(tokenHeader[0] ?? "");

        string? tokenString = tokenMatch.Success ? tokenMatch.Groups[1].Value : null;

        if (tokenString is null)
        {
            return TypedResults.BadRequest();
        }

        var token = JwtUtils.Verify<Tokens.Shared.PlayfabSessionTicket>(tokenString, config.PlayfabApi.SessionTicketSecretBytes);
        if (token is null)
        {
            return TypedResults.Forbid();
        }

        // TODO
        var statistics = new Dictionary<string, int>()
        {
            ["BlocksPlaced"] = 0,
            ["BlocksCollected"] = 0,
            ["Deaths"] = 0,
            ["ItemsCrafted"] = 0,
            ["ItemsSmelted"] = 0,
            ["ToolsBroken"] = 0,
            ["MobsKilled"] = 0,
            ["BuildplateSeconds"] = 0,
            ["SharedBuildplateViews"] = 0,
            ["AdventuresPlayed"] = 0,
            ["TappablesCollected"] = 0,
            ["MobsCollected"] = 0,
            ["ChallengesCompleted"] = 0,
        };

        return JsonPascalCase(new PlayfabOkResponse(
            200,
            "OK",
            new Dictionary<string, object>()
            {
                ["Statistics"] = request.StatisticNames
                    .Where(statistics.ContainsKey)
                    .Select(field => new
                    {
                        StatisticName = field,
                        Value = statistics[field],
                    }),
            }
        ));
    }

    [HttpPost("WritePlayerEvent")]
    public ContentHttpResult WritePlayerEvent()
        => JsonPascalCase(new PlayfabOkResponse(
            200,
            "OK",
            new Dictionary<string, object>()
            {
                ["EventId"] = Guid.NewGuid().ToString("N"),
            }
        ));

    [GeneratedRegex("^[0-9A-F]{16}-(.*)$")]
    private static partial Regex GetAuthRegex();
}
