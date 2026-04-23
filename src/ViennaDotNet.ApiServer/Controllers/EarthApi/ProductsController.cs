using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using ViennaDotNet.ApiServer;
using ViennaDotNet.ApiServer.Types.Catalog;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.Common;
using ViennaDotNet.Common.JsonConverters;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.StaticData;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/products")]
public class ProductsController : ViennaControllerBase
{
    private static Catalog Catalog => Program.staticData.Catalog;

    [HttpPost("getProductInfo")]
    public async Task<Results<ContentHttpResult, BadRequest<string>, InternalServerError>> GetProductInfo()
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest("Invalid login");
        }

        try
        {
            var request = await Request.Body.AsJsonAsync<GetProductInfoRequest>(default);
            if (request is null)
            {
                return TypedResults.BadRequest("Invalid request data");
            }

            var nfcData = request.NfcChip.Data;

            if (nfcData.Length is 0 || nfcData[0][0] > 2 /* URL Record 2 == https */)
            {
                return TypedResults.BadRequest("Scanned Boost Mini did not provide a valid record to identify with");
            }

            var urlInfo = Encoding.UTF8.GetString(nfcData[0].AsSpan(1));

            if (!urlInfo.StartsWith("pid.mattel/"))
            {
                TypedResults.BadRequest("Scanned Boost Minis URL record does not start with pid.mattel");
            }

            var boostIdData = urlInfo[11..];

            boostIdData += string.Join("", Enumerable.Repeat("=", boostIdData.Length % 4));

            var boostIdBytes = new Span<byte>(new byte[boostIdData.Length * 3 / 4]);

            if (Convert.TryFromBase64String(boostIdData, boostIdBytes, out var boostIdByteCount)
                && boostIdByteCount == 24
                && boostIdBytes[0] == 2
                && boostIdBytes[1] == 0)
            {
                var boostIdIntData = boostIdBytes[2..6].ToArray();
                Array.Reverse(boostIdIntData);

                var boostId = BitConverter.ToUInt32(boostIdIntData).ToString();

                var boostUniqueIdData = boostIdBytes[12..20].ToArray();
                Array.Reverse(boostUniqueIdData);

                var uniqueId = BitConverter.ToInt64(boostUniqueIdData).ToString();

                Log.Information($"Boost id: {boostId}, unique id: {uniqueId}");
                if (Catalog.NfcBoostsCatalog.MiniFigs.TryGetValue(boostId, out var product))
                {
                    return JsonCamelCase(new ProductInfo { Id = product.Id, Type = ProductType.NfcMiniFig, UniqueId = uniqueId });
                }

                return TypedResults.BadRequest("Scanned Boost Mini has invalid identifier");
            }

            return TypedResults.BadRequest("Failed to parse boostId from scanned Boost Mini");
        }
        catch (Exception ex)
        {
            Log.Error("Error: " + ex);
            return TypedResults.InternalServerError();
        }
    }

    private sealed record GetProductInfoRequest(
        GetProductInfoRequest.NfcChipR NfcChip
    )
    {
        public sealed record NfcChipR(
            [property: JsonConverter(typeof(NestedByteArrayConverter))]
            byte[][] Data
        );
    }

    public class ProductInfo
    {
        public string Id { get; set; }
        public string UniqueId { get; set; }
        public ProductType Type { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProductType
    {
        MiniFig,
        NfcMiniFig
    }
}