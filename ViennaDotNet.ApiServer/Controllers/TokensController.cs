using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Linq;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using Rewards = ViennaDotNet.ApiServer.Utils.Rewards;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}/player/tokens")]
    public class TokensController : ControllerBase
    {
        private static EarthDB earthDB => Program.DB;

        [HttpGet]
        public IActionResult Get()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            Tokens tokens = (Tokens)new EarthDB.Query(false)
                .Get("tokens", playerId, typeof(Tokens))
                .Execute(earthDB)
                .Get("tokens").Value;

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, Dictionary<string, Token>>()
            {
                {
                    "tokens",
                    tokens.getTokens().Collect(() => new Dictionary<string, Token>(), (hashmap, token) =>
                    {
                        hashmap.Add(token.id, new Token(
                            Enum.Parse<Token.Type>(token.token.type.ToString()),
                            new Dictionary<string, string>(token.token.properties),
                            Rewards.fromDBRewardsModel(token.token.rewards).toApiResponse(),
                            Enum.Parse<Token.Lifetime>(token.token.lifetime.ToString())
                        ));
                    }, DictionaryExtensions.AddRange)
                }
            }, null));
            return Content(resp);
        }
    }
}
