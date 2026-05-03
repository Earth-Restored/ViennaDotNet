using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace Solace.ApiServer.Controllers.XboxLive;

[Route("users")]
[Route("privacy.xboxlive.com/users")]
internal sealed partial class PrivacyController : SolaceControllerBase
{
    private sealed record PeopleResponse(
        object[] Users
    );

    [HttpGet("{xuidParam}/people/avoid")]
    public Results<ContentHttpResult, UnauthorizedHttpResult, BadRequest> GetPeopleAvoid(string xuidParam)
    {
        var authUnion = XboxLiveAuth();
        if (authUnion.IsB)
        {
            return authUnion.B.Result is UnauthorizedHttpResult unauthorized ? unauthorized : (BadRequest)authUnion.B.Result;
        }

        var token = authUnion.A;

        Match xuidMatch = GetXuidRegex().Match(xuidParam);

        string? xuid = xuidMatch.Success ? xuidMatch.Groups[1].Value : null;

        if (xuid is null)
        {
            return TypedResults.BadRequest();
        }

        if (xuid != token.UserId)
        {
            return TypedResults.Unauthorized();
        }

        return JsonCamelCase(new PeopleResponse(
            []
        ));
    }

    [HttpGet("{xuidParam}/people/mute")]
    public Results<ContentHttpResult, UnauthorizedHttpResult, BadRequest> GetPeopleMute(string xuidParam)
    {
        var authUnion = XboxLiveAuth();
        if (authUnion.IsB)
        {
            return authUnion.B.Result is UnauthorizedHttpResult unauthorized ? unauthorized : (BadRequest)authUnion.B.Result;
        }

        var token = authUnion.A;

        Match xuidMatch = GetXuidRegex().Match(xuidParam);

        string? xuid = xuidMatch.Success ? xuidMatch.Groups[1].Value : null;

        if (xuid is null)
        {
            return TypedResults.BadRequest();
        }

        if (xuid != token.UserId)
        {
            return TypedResults.Unauthorized();
        }

        return JsonPascalCase(new PeopleResponse(
            []
        ));
    }

    [GeneratedRegex(@"^xuid\((.*)\)$")]
    private static partial Regex GetXuidRegex();
}
