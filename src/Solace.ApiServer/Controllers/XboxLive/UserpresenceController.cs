using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace Solace.ApiServer.Controllers.XboxLive;

[Route("users")]
[Route("userpresence.xboxlive.com/users")]
internal sealed partial class UserpresenceController : SolaceControllerBase
{
    [HttpPost("{xuidParam}/devices/current/titles/current")]
    public Results<Ok, UnauthorizedHttpResult, BadRequest> GetTitles(string xuidParam)
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

        // TODO

        return TypedResults.Ok();
    }

    // TODO

    [GeneratedRegex(@"^xuid\((.*)\)$")]
    private static partial Regex GetXuidRegex();
}
