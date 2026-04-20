using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ViennaDotNet.ApiServer.Models;
using ViennaDotNet.ApiServer.Utils;

namespace ViennaDotNet.ApiServer.Controllers.XboxLive.Auth;

[Route("user/authenticate")]
[Route("user.auth.xboxlive.com/user/authenticate")]
public class UserController : ViennaControllerBase
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
        Dictionary<string, Dictionary<string, string>[]> DisplayClaims
    );

    [HttpPost]
    public Results<ContentHttpResult, UnauthorizedHttpResult> Authenticate([FromBody] AuthenticateRequest request)
    {
        var ticket = JwtUtils.Verify<Tokens.Shared.XboxTicketToken>(request.Properties.RpsTicket, config.Login.XboxTokenSecretBytes)?.Data;

        if (ticket is null)
        {
            return TypedResults.Unauthorized();
        }

        var tokenValidity = ValidityDatePair.Create(config.XboxLive.TokenValidityMinutes);
        var token = new Tokens.Xbox.UserToken()
        {
            Xid = ticket.UserId,
            Uhs = ticket.UserId,

            UserId = ticket.UserId,
            Username = ticket.Username,
        };

        return JsonPascalCase(new AuthenticateResponse(
            tokenValidity.IssuedStr,
            tokenValidity.ExpiresStr,
            JwtUtils.Sign<Tokens.Xbox.AuthToken>(token, config.XboxLive.AuthTokenSecretBytes, tokenValidity),
            new()
            {
                ["xui"] = [
                    new()
                    {
                        ["uhs"] = token.Uhs,
                    },
                ],
            }
        ));
    }
}
