using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Linq;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
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
                        hashmap[token.id] = new Token(
                            Enum.Parse<Token.Type>(token.token.type.ToString()),
                            new Dictionary<string, string>(token.token.properties),
                            Rewards.fromDBRewardsModel(token.token.rewards).toApiResponse(),
                            Enum.Parse<Token.Lifetime>(token.token.lifetime.ToString())
                        );
                    }, DictionaryExtensions.AddRange)
                }
            }, null));
            return Content(resp, "application/json");
        }

        // TODO: some token types might have actions to perform when they're redeemed?
        [HttpPost]
        [Route("{tokenId}/redeem")]
        public IActionResult Redeem(string tokenId)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            Tokens.Token? token;
            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("tokens", playerId, typeof(Tokens))
                    .Then(results1 =>
                    {
                        Tokens tokens = (Tokens)results1.Get("tokens").Value;
                        Tokens.Token? removedToken = tokens.removeToken(tokenId);
                        if (removedToken != null)
                            return new EarthDB.Query(true)
                                .Update("tokens", playerId, tokens)
                                .Extra("success", true)
                                .Extra("token", removedToken);
                        else
                            return new EarthDB.Query(false)
                                .Extra("success", false);
                    })
                    .Execute(earthDB);
                token = (bool)results.getExtra("success") ? (Tokens.Token)results.getExtra("token") : null;
            }
            catch (EarthDB.DatabaseException ex)
            {
                throw new ServerErrorException(ex);
            }

            if (token != null)
            {
                string resp = JsonConvert.SerializeObject(new Token(
                    Enum.Parse<Token.Type>(token.type.ToString()),
                    new Dictionary<string, string>(token.properties),
                    Rewards.fromDBRewardsModel(token.rewards).toApiResponse(),
                    Enum.Parse<Token.Lifetime>(token.lifetime.ToString())
                ));
                return Content(resp, "application/json");
            }
            else
                return BadRequest();
        }
    }
}
