using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Solace.ApiServer.Models;
using Solace.ApiServer.Utils;

namespace Solace.ApiServer.Controllers.XboxLive.Auth;

[Route("device/authenticate")]
[Route("device.auth.xboxlive.com/device/authenticate")]
public class DeviceController : ViennaControllerBase
{
    private static Config config => Program.config;

    public sealed record AuthenticateRequest(
        AuthenticateRequest.PropertiesR Properties,
        string RelyingParty,
        string TokenType
    )
    {
        public sealed record PropertiesR(
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
    public ContentHttpResult Authenticate([FromBody] AuthenticateRequest request)
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
