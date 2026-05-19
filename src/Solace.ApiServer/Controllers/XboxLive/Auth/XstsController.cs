using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Solace.ApiServer.Models;
using Solace.ApiServer.Utils;

namespace Solace.ApiServer.Controllers.XboxLive.Auth;

[Route("xsts/authorize")]
[Route("xsts.auth.xboxlive.com/xsts/authorize")]
internal sealed class XstsController : SolaceControllerBase
{
    private static Config Config => Program.config;

    internal sealed record AuthenticateRequest(
        AuthenticateRequest.PropertiesR Properties,
        string RelyingParty,
        string TokenType
    )
    {
        internal sealed record PropertiesR(
            string SandboxId,
            string DeviceToken,
            string TitleToken,
            string[] UserTokens
        );
    }

    private sealed record AuthenticateResponse(
        string IssueInstant,
        string NotAfter,
        string Token,
        Dictionary<string, Dictionary<string, string>[]> DisplayClaims
    );

    [HttpPost]
    public Results<ContentHttpResult, UnauthorizedHttpResult, BadRequest> Authenticate([FromBody] AuthenticateRequest request)
    {
        if (request.Properties.UserTokens.Length is not 1)
        {
            return TypedResults.BadRequest();
        }

        var deviceTokenAuth = JwtUtils.Verify<Tokens.Xbox.AuthToken>(request.Properties.DeviceToken, Config.XboxLive.AuthTokenSecretBytes)?.Data;
        var titleTokenAuth = JwtUtils.Verify<Tokens.Xbox.AuthToken>(request.Properties.TitleToken, Config.XboxLive.AuthTokenSecretBytes)?.Data;
        var userTokenAuth = JwtUtils.Verify<Tokens.Xbox.AuthToken>(request.Properties.UserTokens[0], Config.XboxLive.AuthTokenSecretBytes)?.Data;

        if (deviceTokenAuth is not Tokens.Xbox.DeviceToken || titleTokenAuth is not Tokens.Xbox.TitleToken || userTokenAuth is not Tokens.Xbox.UserToken userToken)
        {
            return TypedResults.Unauthorized();
        }

        switch (request.RelyingParty)
        {
            case "http://xboxlive.com":
                {
                    var tokenValidity = ValidityDatePair.Create(Config.XboxLive.TokenValidityMinutes);
                    var token = new Tokens.Xbox.XapiToken(userToken.UserId, userToken.Username);

                    return JsonPascalCase(new AuthenticateResponse(
                        tokenValidity.IssuedStr,
                        tokenValidity.ExpiresStr,
                        JwtUtils.Sign(token, Config.XboxLive.XapiTokenSecretBytes, tokenValidity),
                        new()
                        {
                            ["xui"] = [
                                new()
                                {
                                    ["xid"] = userToken.Xid.ToString(),
                                    ["uhs"] = userToken.Uhs.ToString(),

                                    ["gtg"] = userToken.Username,
                                    ["agg"] = "Adult",

                                    ["usr"] = "185 190 234",
                                    ["prv"] = "184 186 187 188 191 193 195 196 198 199 200 201 203 204 205 206 208 211 217 220 224 227 228 235 238 245 247 249 252 254 255"
                                },
                            ]
                        }
                    ));
                }

            case "http://events.xboxlive.com":
                {
                    var tokenValidity = ValidityDatePair.Create(Config.XboxLive.TokenValidityMinutes);
                    var token = new Tokens.Xbox.XapiToken(userToken.UserId, userToken.Username);

                    return JsonPascalCase(new AuthenticateResponse(
                       tokenValidity.IssuedStr,
                       tokenValidity.ExpiresStr,
                       JwtUtils.Sign(token, Config.XboxLive.XapiTokenSecretBytes, tokenValidity),
                       new()
                       {
                           ["xui"] = [
                                new()
                                {
                                    ["uhs"] = userToken.Uhs.ToString(),
                                },
                           ]
                       }
                   ));
                }

            case "https://b980a380.minecraft.playfabapi.com/":
                {
                    var tokenValidity = ValidityDatePair.Create(Config.XboxLive.TokenValidityMinutes);
                    var token = new Tokens.Shared.PlayfabXboxToken(userToken.UserId);

                    return JsonPascalCase(new AuthenticateResponse(
                       tokenValidity.IssuedStr,
                       tokenValidity.ExpiresStr,
                       JwtUtils.Sign(token, Config.XboxLive.PlayfabTokenSecretBytes, tokenValidity),
                       new()
                       {
                           ["xui"] = [
                                new()
                                {
                                    ["uhs"] = userToken.Uhs.ToString(),
                                },
                           ]
                       }
                   ));
                }

            default:
                return TypedResults.BadRequest();
        }
    }
}
