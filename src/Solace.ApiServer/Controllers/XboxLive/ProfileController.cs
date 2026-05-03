using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Solace.ApiServer.Models;
using Solace.Common.Utils;

namespace Solace.ApiServer.Controllers.XboxLive;

[Route("users")]
[Route("profile.xboxlive.com/users")]
internal sealed partial class ProfileController : SolaceControllerBase
{
    private readonly LiveDbContext _dbContext;

    public ProfileController(LiveDbContext context)
    {
        _dbContext = context;
    }

    private sealed record ProfileSettingsResponse(
        IEnumerable<ProfileUser> ProfileUsers
    );

    private sealed record ProfileUser(
        string Id,
        string HostId,
        IEnumerable<ProfileSetting> Settings,
        bool IsSponsoredUser
    );

    internal sealed record BatchProfileSettingsRequest(
        string[] Settings,
        string[] UserIds
    );

    [HttpPost("batch/profile/settings")]
    public async Task<Results<ContentHttpResult, NotFound, UnauthorizedHttpResult, BadRequest>> GetBatchProfileSettings()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<BatchProfileSettingsRequest>(cancellationToken);

        if (request is null)
        {
            return TypedResults.BadRequest();
        }

        var authUnion = XboxLiveAuth();
        if (authUnion.IsB)
        {
            return authUnion.B.Result is UnauthorizedHttpResult unauthorized ? unauthorized : (BadRequest)authUnion.B.Result;
        }

        var token = authUnion.A;

        foreach (string userId in request.UserIds)
        {
            if (userId != token.UserId)
            {
                return TypedResults.Unauthorized();
            }
        }

        var account = await _dbContext.Accounts
            .FirstOrDefaultAsync(account => account.Id == token.UserId, cancellationToken);

        if (account is null)
        {
            return TypedResults.NotFound();
        }

        return JsonCamelCase(new ProfileSettingsResponse(
            request.UserIds.Select(userId
                => new ProfileUser(
                    userId,
                    userId,
                    GetProfileFields(account, request.Settings),
                    false
                )
            )
        ));
    }

    [HttpGet("{gtParam}/profile/settings")]
    public async Task<Results<ContentHttpResult, NotFound, UnauthorizedHttpResult, BadRequest>> GetProfileSettings(string gtParam)
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var authUnion = XboxLiveAuth();
        if (authUnion.IsB)
        {
            return authUnion.B.Result is UnauthorizedHttpResult unauthorized ? unauthorized : (BadRequest)authUnion.B.Result;
        }

        var token = authUnion.A;

        string? gt;
        if (gtParam == "me")
        {
            gt = token.Username;
        }
        else
        {
            Match gtMatch = GetGtRegex().Match(gtParam);

            gt = gtMatch.Success ? gtMatch.Groups[1].Value : null;
        }

        if (gt != token.Username)
        {
            return TypedResults.Unauthorized();
        }

        if (!Request.Query.TryGetValue("settings", out var settings))
        {
            return TypedResults.BadRequest();
        }

        var account = await _dbContext.Accounts
            .FirstOrDefaultAsync(account => account.Id == token.UserId, cancellationToken);

        if (account is null)
        {
            return TypedResults.NotFound();
        }

        return JsonCamelCase(new ProfileSettingsResponse([
            new ProfileUser(
                token.UserId,
                token.UserId,
                GetProfileFields(account, settings[0]?.Split(',') ?? []),
                false
            ),
        ]));
    }

    private Dictionary<string, string> GetProfile(Account account)
        => new Dictionary<string, string>()
        {
            ["AppDisplayName"] = account.Username,
            ["AppDisplayPicRaw"] = $"{(Request.IsHttps ? "https://" : "http://")}{Request.Host.Value}/{account.ProfilePictureUrl}",
            ["GameDisplayName"] = account.Username,
            ["GameDisplayPicRaw"] = $"{(Request.IsHttps ? "https://" : "http://")}{Request.Host.Value}/{account.ProfilePictureUrl}",
            ["Gamertag"] = account.Username,
            ["Gamerscore"] = "69",
            ["FirstName"] = account.FirstName ?? account.Username,
            ["LastName"] = account.LastName ?? account.Username,
            ["SpeechAccessibility"] = "",
        };

    private IEnumerable<ProfileSetting> GetProfileFields(Account account, IEnumerable<string> fields)
    {
        var profile = GetProfile(account);

        return fields
            .Where(profile.ContainsKey)
            .Select(field => new ProfileSetting(field, profile[field]));
    }

    [GeneratedRegex(@"^gt\((.*)\)$")]
    private static partial Regex GetGtRegex();

    private sealed record ProfileSetting(
        string Id,
        string Value
    );
}
