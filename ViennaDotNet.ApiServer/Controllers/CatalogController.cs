using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ViennaDotNet.ApiServer.Utils;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}")]
    public class CatalogController : ControllerBase
    {
        private static Catalog catalog => Program.Catalog;

        [Route("inventory/catalogv3")]
        public IActionResult GetItemsCatalog()
        {
            return Content(JsonConvert.SerializeObject(new EarthApiResponse(catalog.itemsCatalog)), "application/json");
        }

        [Route("recipes")]
        public IActionResult GetRecipeCatalog()
        {
            return Content(JsonConvert.SerializeObject(new EarthApiResponse(catalog.recipesCatalog)), "application/json");
        }

        [Route("journal/catalog")]
        public IActionResult GetJournalCatalog()
        {
            return Content(JsonConvert.SerializeObject(new EarthApiResponse(catalog.journalCatalog)), "application/json");
        }

        [Route("products/catalog")]
        public IActionResult GetNFCBoostsCatalog()
        {
            return Content(JsonConvert.SerializeObject(new EarthApiResponse(catalog.nfcBoostsCatalog)), "application/json");
        }
    }
}
