using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Solace.ApiServer.Models.Playfab;

namespace Solace.ApiServer.Controllers.PlayfabApi;

[Route("inventory")]
[Route("20CA2.playfabapi.com/inventory")]
internal sealed class InventoryController : SolaceControllerBase
{
    [HttpPost("GetVirtualCurrencies")]
    public ContentHttpResult GetVirtualCurrencies()
        => JsonPascalCase(new PlayfabOkResponse(
            200,
            "OK",
            new Dictionary<string, object>()
            {
                ["Currencies"] = (IEnumerable<object>)[new Dictionary<string, object>() {
                    ["CurrencyId"] = "ecd19d3c-7635-402c-a185-eb11cb6c6946",
                    ["Amount"] = 0,
                    ["ChangedAmount"] = 0,
                }],
                ["Items"] = Array.Empty<object>(),
            }
        ));

    [HttpPost("redeem")]
    public ContentHttpResult Redeem()
        => JsonPascalCase(new PlayfabOkResponse(
            200,
            "OK",
            new Dictionary<string, object>()
            {
                ["Succeeded"] = Array.Empty<object>(),
                ["Failed"] = Array.Empty<object>(),
            }
        ));

    [HttpPost("GetInventoryItems")]
    public ContentHttpResult GetInventoryItems()
        => JsonPascalCase(new PlayfabOkResponse(
            200,
            "OK",
            new Dictionary<string, object>()
            {
                ["Items"] = Array.Empty<object>(),
                ["ETag"] = "1/MQ==",
                ["ItemMetadata"] = Array.Empty<object>(),
                ["Subscriptions"] = Array.Empty<object>(),
            }
        ));
}
