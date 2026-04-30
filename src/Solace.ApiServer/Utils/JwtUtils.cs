using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ViennaDotNet.ApiServer.Models;
using ViennaDotNet.Common;

namespace ViennaDotNet.ApiServer.Utils;

internal static class JwtUtils
{
    private static readonly JwtSecurityTokenHandler jwtHandler = new JwtSecurityTokenHandler();

    public static string Sign<TData>(Token<TData> token, byte[] secret)
        where TData : ITokenData<TData>
        => SignInternal<TData>(token, secret, new ValidityDatePair(token.Issued, token.Expires));

    public static string Sign<TData>(TData data, byte[] secret, ValidityDatePair validity)
        where TData : ITokenData<TData>
        => SignInternal<TData>(data, secret, validity);

    private static string SignInternal<TData>(object dataOrToken, byte[] secret, ValidityDatePair validity)
        where TData : ITokenData<TData>
    {
        TData data = dataOrToken switch
        {
            Token<TData> token => token.Data,
            TData tokenData => tokenData,
            _ => throw new UnreachableException(),
        };

        Claim[] payload =
        [
            new Claim("iat", validity.Issued.ToUnixTimeSeconds().ToString()),
            new Claim("nbf", validity.Issued.ToUnixTimeSeconds().ToString()),
            new Claim("exp", validity.Expires.ToUnixTimeSeconds().ToString()),
            new Claim("data", Json.Serialize(data)),
        ];

        return jwtHandler.WriteToken(new JwtSecurityToken(
            new JwtHeader(new SigningCredentials(
                new SymmetricSecurityKey(secret),
                SecurityAlgorithms.HmacSha256)),
            new JwtPayload(payload)
        ));
    }

    public static Token<TData>? Verify<TData>(string token, byte[] secret, bool allowExpired = false)
        where TData : ITokenData<TData>
    {
        try
        {
            var claims = jwtHandler.ValidateToken(token, new TokenValidationParameters()
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = !allowExpired,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(secret),
            }, out _).Claims.ToDictionary(claim => claim.Type, claim => claim.Value);

            if (!claims.TryGetValue("iat", out string? iat) || !claims.TryGetValue("exp", out string? exp) || !claims.TryGetValue("data", out string? dataJson))
            {
                return null;
            }

            if (!long.TryParse(iat, out long issuedSeconds) || !long.TryParse(exp, out long expiresSeconds))
            {
                return null;
            }

            var expires = DateTimeOffset.FromUnixTimeSeconds(expiresSeconds);

            var data = Json.Deserialize<TData>(dataJson);
            if (data is null)
            {
                return null;
            }

            return new Token<TData>(DateTimeOffset.FromUnixTimeSeconds(issuedSeconds), expires, allowExpired && expires < DateTimeOffset.UtcNow, data);
        }
        catch (SecurityTokenException ex)
        {
            Log.Debug($"JWT verification failed: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            Log.Debug($"JWT data deserialization failed: {ex.Message}");
            return null;
        }
    }
}
