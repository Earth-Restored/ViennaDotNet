using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Solace.ApiServer.Models;
using Solace.ApiServer.Models.Playfab;
using Solace.ApiServer.Utils;
using Solace.Common.Utils;

namespace Solace.ApiServer.Controllers.PlayfabApi;

[Route("Client")]
[Route("20CA2.playfabapi.com/Client")]
internal sealed partial class LoginController : SolaceControllerBase
{
    private static Config config => Program.config;

    private readonly LiveDbContext _dbContext;

    public LoginController(LiveDbContext context)
    {
        _dbContext = context;
    }

    private sealed record LoginWithCustomIDRequest(
        string TitleId,
        object? EncryptedRequest,
        object? PlayerSecret,
        bool CreateAccount,
        string CustomId
    );

    [HttpPost("LoginWithCustomID")]
    public async Task<Results<ContentHttpResult, BadRequest>> LoginWithCustomID()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<LoginWithCustomIDRequest>(cancellationToken);

        if (request is null || !GetTitleIdRegex().IsMatch(request.TitleId))
        {
            return TypedResults.BadRequest();
        }

        return JsonCamelCase(new PlayfabErrorResponse(
            403,
            "Forbidden",
            "NotAuthorizedByTitle",
            1191,
            "Action not authorized by title",
            null
        ));
    }

    private sealed record LoginWithXboxRequest(
        string TitleId,
        object? EncryptedRequest,
        object? PlayerSecret,
        bool CreateAccount,
        string XboxToken
    );

    [HttpPost("LoginWithXbox")]
    public async Task<Results<ContentHttpResult, ForbidHttpResult, NotFound, BadRequest>> LoginWithXbox()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<LoginWithXboxRequest>(cancellationToken);

        if (request is null || !GetTitleIdRegex().IsMatch(request.TitleId))
        {
            return TypedResults.BadRequest();
        }

        var authorization = XboxAuthorizationUtils.Parse(request.XboxToken);

        if (authorization is not { } authValue)
        {
            return TypedResults.BadRequest();
        }

        var xboxToken = JwtUtils.Verify<Tokens.Shared.PlayfabXboxToken>(authValue.TokenString, config.XboxLive.PlayfabTokenSecretBytes);

        if (xboxToken is null || xboxToken.Data.UserId != authValue.UserId)
        {
            // TODO: probably supposed to use a "fake 403" as with LoginWithCustomID
            return TypedResults.Forbid();
        }

        string userId = xboxToken.Data.UserId;

        var account = await _dbContext.Accounts
            .FirstOrDefaultAsync(account => account.Id == userId, cancellationToken);

        if (account is null)
        {
            return TypedResults.NotFound();
        }

        var sessionTicketValidity = ValidityDatePair.Create(config.PlayfabApi.SessionTicketValidityMinutes);
        var sessionTicket = new Tokens.Shared.PlayfabSessionTicket(userId);
        string sessionTicketString = JwtUtils.Sign(sessionTicket, config.PlayfabApi.SessionTicketSecretBytes, sessionTicketValidity);

        var entityTokenValidity = ValidityDatePair.Create(config.PlayfabApi.EntityTokenValidityMinutes);
        var entityToken = new Tokens.Playfab.EntityToken(userId, "title_player_account");
        string entityTokenString = JwtUtils.Sign(entityToken, config.PlayfabApi.EntityTokenSecretBytes, entityTokenValidity);

        return JsonPascalCase(new PlayfabOkResponse(
            200,
            "OK",
            new Dictionary<string, object>()
            {
                ["SessionTicket"] = $"{userId.ToUpperInvariant()}-{sessionTicketString}",
                ["PlayFabId"] = userId,
                ["NewlyCreated"] = false,
                ["SettingsForUser"] = new Dictionary<string, bool>()
                {
                    ["NeedsAttribution"] = false,
                    ["GatherDeviceInfo"] = true,
                    ["GatherFocusInfo"] = true,
                },
                ["LastLoginTime"] = DateTimeOffset.FromUnixTimeSeconds(account.CreatedDate).UtcDateTime,
                ["InfoResultPayload"] = new Dictionary<string, object>()
                {
                    ["AccountInfo"] = new Dictionary<string, object>()
                    {
                        ["PlayFabId"] = userId,
                        ["Created"] = DateTimeOffset.FromUnixTimeSeconds(account.CreatedDate).UtcDateTime,
                        ["TitleInfo"] = new Dictionary<string, object>()
                        {
                            ["Origination"] = "XboxLive",
                            ["Created"] = DateTimeOffset.FromUnixTimeSeconds(account.CreatedDate).UtcDateTime,
                            ["LastLogin"] = DateTimeOffset.FromUnixTimeSeconds(account.CreatedDate).UtcDateTime,
                            ["FirstLogin"] = DateTimeOffset.FromUnixTimeSeconds(account.CreatedDate).UtcDateTime,
                            ["isBanned"] = false,
                            ["TitlePlayerAccount"] = new Dictionary<string, string>()
                            {
                                ["Id"] = userId,
                                ["Type"] = "title_player_account",
                                ["TypeString"] = "title_player_account",
                            },
                        },
                        ["PrivateInfo"] = new object(),
                        ["XboxInfo"] = new Dictionary<string, string>()
                        {
                            ["XboxUserId"] = userId,
                            ["XboxUserSandbox"] = "RETAIL",
                        },
                    },
                    ["UserInventory"] = Array.Empty<object>(),
                    ["UserDataVersion"] = 0,
                    ["UserReadOnlyDataVersion"] = 0,
                    ["CharacterInventories"] = Array.Empty<object>(),
                    ["PlayerProfile"] = new Dictionary<string, string>()
                    {
                        ["PublisherId"] = "B63A0803D3653643",
                        ["TitleId"] = request.TitleId,
                        ["PlayerId"] = userId,
                    },
                },
                ["EntityToken"] = new Dictionary<string, object>()
                {
                    ["EntityToken"] = entityTokenString,
                    ["TokenExpiration"] = entityTokenValidity.ExpiresDT,
                    ["Entity"] = new Dictionary<string, string>()
                    {
                        ["Id"] = entityToken.Id,
                        ["Type"] = entityToken.Type,
                        ["TypeString"] = entityToken.Type,
                    },
                },
                ["TreatmentAssignment"] = new Dictionary<string, object>()
                {
                    ["Variants"] = Array.Empty<object>(),
                    ["Variables"] = Array.Empty<object>(),
                },
            }
        ));
    }

    [HttpPost("LinkXboxAccount")]
    public ContentHttpResult LinkXboxAccount()
        => JsonCamelCase(new PlayfabErrorResponse(
            401,
            "Unauthorized",
            "NotAuthenticated",
            1074,
            "This API method does not allow anonymous callers.",
            null
        ));

    [GeneratedRegex("^[0-9A-F]{5}$")]
    private static partial Regex GetTitleIdRegex();
}
