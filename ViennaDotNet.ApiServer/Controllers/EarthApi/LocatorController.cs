using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using ViennaDotNet.ApiServer.Types;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;

namespace ViennaDotNet.ApiServer.Controllers.EarthApi;

[ApiController]
[ApiVersion("1.0")]
[ApiVersion("1.1")]
[Route("player/environment")]
public class LocatorController : ControllerBase
{
    [HttpGet]
    public ContentResult Get()
    {
        string protocol = Request.IsHttps ? "https://" : "http://";
        string baseServerIP = $"{protocol}{Request.Host.Value}";
        Log.Information($"{HttpContext.Connection.RemoteIpAddress} has issued locator, replying with {baseServerIP}");

        string resp = Json.Serialize(new EarthApiResponse(new LocatorResponse(new()
        {
            ["production"] = new LocatorResponse.Environment(baseServerIP, baseServerIP + "/cdn", "20CA2"),
        },
        new()
        {
            ["2020.1217.02"] = ["production"],
            ["2020.1210.01"] = ["production"],
        }
        )));
        return Content(resp, "application/json");
    }
}

[ApiController]
[ApiVersion("1.0")]
[ApiVersion("1.1")]
[Route("/api/v1.1/player/environment")]
public class MojankLocatorController : ControllerBase
{
    [HttpGet]
    public ContentResult Get()
    {
        string protocol = Request.IsHttps ? "https://" : "http://";
        string baseServerIP = $"{protocol}{Request.Host.Value}";
        Log.Information($"{HttpContext.Connection.RemoteIpAddress} has issued locator, replying with {baseServerIP}");

        string resp = Json.Serialize(new EarthApiResponse(new LocatorResponse(new()
        {
            ["production"] = new LocatorResponse.Environment(baseServerIP, baseServerIP + "/cdn", "20CA2"),
        },
        new()
        {
            ["2020.1217.02"] = ["production"],
            ["2020.1210.01"] = ["production"],
        }
        )));
        return Content(resp, "application/json");
    }
}
