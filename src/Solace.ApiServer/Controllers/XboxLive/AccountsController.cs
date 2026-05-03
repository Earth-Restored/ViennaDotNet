using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Solace.ApiServer.Controllers.XboxLive;

[Route("")]
[Route("accounts.xboxlive.com")]
internal sealed class AccountsController : SolaceControllerBase
{
    private readonly LiveDbContext _dbContext;

    public AccountsController(LiveDbContext context)
    {
        _dbContext = context;
    }

    private sealed record ProfileResponse(
        string? GamerTag,
        string? MidasConsole,
        DateTime TouAcceptanceDate,
        string? GamerTagChangeReason,
        DateTime DateOfBirth,
        DateTime DateCreated,
        string? Email,
        string? FirstName,
        string? HomeAddressInfo,
        string? HomeConsole,
        string? ImageUrl,
        bool IsAdult,
        string? LastName,
        string? LegalCountry,
        string? Locale,
        bool? MsftOptin,
        string? OwnerHash,
        string? OwnerXuid,
        bool? PartnerOptin,
        bool RequirePasskeyForPurchase,
        bool RequirePasskeyForSignIn,
        string? SubscriptionEntitlementInfo,
        string UserHash,
        string? UserKey,
        int UserXuid
    );

    [HttpGet("users/current/profile")]
    public async Task<Results<ContentHttpResult, NotFound, UnauthorizedHttpResult, BadRequest>> GetProfile()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var authUnion = XboxLiveAuth();
        if (authUnion.IsB)
        {
            return authUnion.B.Result is UnauthorizedHttpResult unauthorized ? unauthorized : (BadRequest)authUnion.B.Result;
        }

        var token = authUnion.A;

        var account = await _dbContext.Accounts
            .FirstOrDefaultAsync(account => account.Id == token.UserId, cancellationToken);

        if (account is null)
        {
            return TypedResults.NotFound();
        }

        return JsonCamelCase(new ProfileResponse(
            GamerTag: account.Username,
            MidasConsole: null,
            TouAcceptanceDate: new DateTime(1, 1, 1),
            GamerTagChangeReason: null,
            DateOfBirth: new DateTime(1, 1, 1),
            DateCreated: DateTimeOffset.FromUnixTimeSeconds(account.CreatedDate).UtcDateTime,
            Email: null,
            FirstName: account.FirstName,
            HomeAddressInfo: null,
            HomeConsole: null,
            ImageUrl: null,
            IsAdult: true,
            LastName: account.LastName,
            LegalCountry: null,
            Locale: null,
            MsftOptin: null,
            OwnerHash: null,
            OwnerXuid: null,
            PartnerOptin: null,
            RequirePasskeyForPurchase: false,
            RequirePasskeyForSignIn: false,
            SubscriptionEntitlementInfo: null,
            UserHash: token.UserId,
            UserKey: null,
            UserXuid: 0
        ));
    }
}
