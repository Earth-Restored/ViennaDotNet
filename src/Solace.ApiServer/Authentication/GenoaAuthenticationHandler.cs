using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace ViennaDotNet.ApiServer.Authentication;

public class GenoaAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public GenoaAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        await Task.Yield();

        // skip authentication if endpoint has [AllowAnonymous] attribute
        var endpoint = Context.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() is not null)
            return AuthenticateResult.NoResult();

        // Check if we should really authenticate
        if (endpoint?.Metadata?.GetMetadata<IAuthorizeData>() is null)
            return AuthenticateResult.NoResult();

        if (!Request.Headers.ContainsKey("Authorization"))
            return AuthenticateResult.Fail("Missing Authorization Header");

        string? id;
        try
        {
            if (!Request.Headers.TryGetValue("Authorization", out StringValues authorization))
                return AuthenticateResult.Fail("Invalid Authorization Header");

            var authHeader = AuthenticationHeaderValue.Parse(authorization.ToString());
            if (authHeader.Scheme == "Genoa")
                id = authHeader.Parameter;
            else
                return AuthenticateResult.Fail("Invalid Authorization Header");
        }
        catch
        {
            return AuthenticateResult.Fail("Invalid Authorization Header");
        }

        if (id is null)
            return AuthenticateResult.Fail("Invalid Authorization Header");

        // should be lower probably, so it is
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, id.ToLowerInvariant()), };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
