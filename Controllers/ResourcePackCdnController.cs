using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ViennaDotNet.Controllers
{
    //Wheres the resource pack?
    [ApiVersion("1.1")]
    [Route("api/v{version:apiVersion}/resourcepacks/2020.1217.02/default")]
    public class ResourcePackController : ControllerBase
    {
        [HttpGet]
        public ContentResult Get()
        {
            JObject response = new JObject(
                new JProperty("result", new JArray(
                    new JObject(
                        new JProperty("order", 0),
                        new JProperty("parsedResourcePackVersion", JToken.FromObject(new List<int>() { 2020, 1214, 4 })),
                        new JProperty("relativePath", "availableresourcepack/resourcepacks/dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35"),//Naming the endpoint the same thing for consistency. this might not be needed. 
                        new JProperty("resourcePackVersion", "2020.1214.04"),
                        new JProperty("resourcePackId", "dba38e59-091a-4826-b76a-a08d7de5a9e2")
                    )
                )),
                new JProperty("expiration", null),
                new JProperty("continuationToken", null),
                new JProperty("updates", new Dictionary<string, string>())
            );

            return Content(JsonConvert.SerializeObject(response), "application/json");
        }
    }

    //Heres the resource pack!
    [Route("cdn/availableresourcepack/resourcepacks/dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35")]
    public class ResourcePackCdnController : ControllerBase
    {
        public IActionResult Get()
        {
            string resourcePackFilePath = @"./data/resourcepacks/vanilla.zip"; //resource packs are distributed as renamed zip files containing an MCpack

            if (!System.IO.File.Exists(resourcePackFilePath))
            {
                Log.Error("[Resourcepacks] Error! Resource pack file not found.");
                return BadRequest(); //we cannot serve you.
            }

            byte[] fileData = System.IO.File.ReadAllBytes(resourcePackFilePath); //Namespaces
            var cd = new System.Net.Mime.ContentDisposition { FileName = "dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35", Inline = true };
            Response.Headers.Add("Content-Disposition", cd.ToString());


            return File(fileData, "application/octet-stream", "dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35");
        }
    }
}
