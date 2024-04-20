using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;

namespace ViennaDotNet.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}/player/tokens")]
    public class TokensController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            Tokens tokens = new EarthDB.Query(false)
                .Get("tokens", playerId, typeof(Tokens))
                .Execute(earthDB)
                .Get("tokens").Value;


            return NotFound();
        }
    }
}
