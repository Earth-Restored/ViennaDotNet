using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using ViennaDotNet.ApiServer.Utils;

namespace ViennaDotNet.ApiServer.Controllers.EarthApi;

[ApiVersion("1.1")]
[Route("cdn/tile/16/{_}/{tilePos1}_{tilePos2}_16.png")]
[ResponseCache(Duration = 11200)]
public class CdnTileController : ViennaControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTile(int _, int tilePos1, int tilePos2, CancellationToken cancellationToken) // _ used because we dont care :|
    {
        if (!await TileUtils.TryWriteTile(tilePos1, tilePos2, Response.Body, cancellationToken))
        {
            return NotFound();
        }

        var cd = new System.Net.Mime.ContentDisposition { FileName = tilePos1 + "_" + tilePos2 + "_16.png", Inline = true };
        Response.Headers.Append("Content-Disposition", cd.ToString());
        Response.Headers.ContentType = "application/octet-stream";

        return new EmptyResult();
    }
}
