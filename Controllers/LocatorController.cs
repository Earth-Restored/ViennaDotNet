using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ViennaDotNet.Controllers
{
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

            JObject response = new JObject(
                new JProperty("result", new JObject(
                    new JProperty("serviceEnvironments", new JObject(
                        new JProperty("production", new JObject(
                            new JProperty("serviceUri", baseServerIP),
                            new JProperty("cdnUri", baseServerIP + "/cdn"),
                            new JProperty("playfabTitleId", "20CA2")    
                        ))
                    )),
                    new JProperty("supportedEnvironments", JToken.FromObject(new Dictionary<string, List<string>>() { { "2020.1217.02", new List<string>() { "production" } }, { "2020.1210.01", new List<string>() { "production" } } }))
                )),
                new JProperty("expiration", null),
                new JProperty("continuationToken", null),
                new JProperty("updates", null)
            );

            var resp = JsonConvert.SerializeObject(response);
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

            JObject response = new JObject(
                new JProperty("result", new JObject(
                    new JProperty("serviceEnvironments", new JObject(
                        new JProperty("production", new JObject(
                            new JProperty("serviceUri", baseServerIP),
                            new JProperty("cdnUri", baseServerIP + "/cdn"),
                            new JProperty("playfabTitleId", "20CA2")
                        ))
                    )),
                    new JProperty("supportedEnvironments", JToken.FromObject(new Dictionary<string, List<string>>() { { "2020.1217.02", new List<string>() { "production" } }, { "2020.1210.01", new List<string>() { "production" } } }))
                ))
            );

            var resp = JsonConvert.SerializeObject(response);
            return Content(resp, "application/json"); ;
        }
    }
}
