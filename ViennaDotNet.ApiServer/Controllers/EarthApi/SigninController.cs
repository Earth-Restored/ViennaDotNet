using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Text.RegularExpressions;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.ApiServer.Controllers;

[ApiVersion("1.1")]
public partial class SigninController : ViennaControllerBase
{
    [GeneratedRegex("^[0-9A-F]{16}$")]
    private static partial Regex GetUserIdRegex();

    [HttpPost("api/v{version:apiVersion}/player/profile/{profileID}")]
    public async Task<IActionResult> Post(string profileID, CancellationToken cancellationToken)
    {
        if (profileID != "signin")
        {
            return BadRequest();
        }

        SigninRequest? signinRequest = await Request.Body.AsJsonAsync<SigninRequest>(cancellationToken);

        string[]? parts = null;
        if (signinRequest is null || (parts = signinRequest.SessionTicket.Split('-')).Length < 2)
        {
            Log.Error($"Sign in request null or parts bad ({parts?.Length ?? -1})");
            return BadRequest();
        }

        string userId = parts[0];
        if (!GetUserIdRegex().IsMatch(userId))
        {
            Log.Error($"User id not match ({userId})");
            return BadRequest();
        }

        // TODO: check credentials

        // TODO: generate secure session token
        string token = userId.ToUpperInvariant();

        return EarthJson(new Dictionary<string, object?>()
        {
            ["authenticationToken"] = token,
            ["basePath"] = "/1",
            ["clientProperties"] = new object(),
            ["mixedReality"] = null,
            ["mrToken"] = null,
            ["streams"] = null,
            ["tokens"] = new object(),
            ["updates"] = new object(),
        });
    }

    private sealed record SigninRequest(string SessionTicket);
}
