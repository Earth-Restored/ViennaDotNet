using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Solace.ApiServer.Models;
using Solace.ApiServer.Utils;

namespace Solace.ApiServer.Controllers.XboxLive.Auth;

[Route("device/authenticate")]
[Route("device.auth.xboxlive.com/device/authenticate")]
internal sealed class DeviceController : SolaceControllerBase
{
    private static Config config => Program.config;

    internal sealed record AuthenticateRequest(
        AuthenticateRequest.PropertiesR Properties,
        string RelyingParty,
        string TokenType
    )
    {
        internal sealed record PropertiesR(
            string AuthMethod,
            string RpsTicket,
            string SiteName
        );
    }

    private sealed record AuthenticateResponse(
        string IssueInstant,
        string NotAfter,
        string Token,
        Dictionary<string, Dictionary<string, string>> DisplayClaims
    );

    [HttpPost]
#pragma warning disable IDE0060 // Remove unused parameter
    public ContentHttpResult Authenticate([FromBody] AuthenticateRequest request)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        var tokenValidity = ValidityDatePair.Create(config.XboxLive.TokenValidityMinutes);
        var token = new Tokens.Xbox.DeviceToken()
        {
            Did = "F700F376F3793B3A", // TODO
        };

        return JsonPascalCase(new AuthenticateResponse(
              tokenValidity.IssuedStr,
              tokenValidity.ExpiresStr,
              JwtUtils.Sign<Tokens.Xbox.AuthToken>(token, config.XboxLive.AuthTokenSecretBytes, tokenValidity),
              new()
              {
                  ["xdi"] = new()
                  {
                      ["did"] = token.Did,
                  },
              }
        ));
    }
}
