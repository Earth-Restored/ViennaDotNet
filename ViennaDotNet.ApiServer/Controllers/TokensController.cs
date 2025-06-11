using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using Rewards = ViennaDotNet.ApiServer.Utils.Rewards;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/player/tokens")]
public class TokensController : ControllerBase
{
    private static EarthDB earthDB => Program.DB;
    private static StaticData.StaticData staticData => Program.staticData;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
            return BadRequest();

        Tokens tokens = (Tokens)(await new EarthDB.Query(false)
            .Get("tokens", playerId, typeof(Tokens))
            .ExecuteAsync(earthDB, cancellationToken))
            .Get("tokens").Value;

        string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Dictionary<string, Dictionary<string, Token>>()
        {
            {
                "tokens",
                tokens.getTokens().Collect(() => new Dictionary<string, Token>(), (hashmap, token) =>
                {
                    hashmap[token.id] = tokenToApiResponse(token.token);
                }, DictionaryExtensions.AddRange)
            }
        }, null));
        return Content(resp, "application/json");
    }

    [HttpPost("{tokenId}/redeem")]
    public async Task<IActionResult> Redeem(string tokenId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
            return BadRequest();

        // request.timestamp
        long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

        Tokens.Token? token;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("tokens", playerId, typeof(Tokens))
                .Then(results1 =>
                {
                    Tokens tokens = (Tokens)results1.Get("tokens").Value;
                    Tokens.Token? removedToken = tokens.removeToken(tokenId);
                    if (removedToken != null)
                    {
                        return new EarthDB.Query(true)
                            .Update("tokens", playerId, tokens)
                            .Then(TokenUtils.doActionsOnRedeemedToken(removedToken, playerId, requestStartedOn, staticData), false)
                            .Extra("success", true)
                            .Extra("token", removedToken);
                    }
                    else
                    {
                        return new EarthDB.Query(false)
                            .Extra("success", false);
                    }
                })
                .ExecuteAsync(earthDB, cancellationToken);
            token = (bool)results.getExtra("success") ? (Tokens.Token)results.getExtra("token") : null;
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        if (token != null)
        {
            string resp = JsonConvert.SerializeObject(tokenToApiResponse(token));
            return Content(resp, "application/json");
        }
        else
            return BadRequest();
    }

    private static Token tokenToApiResponse(Tokens.Token token)
    {
        Dictionary<string, string> properties = [];
        switch (token.type)
        {
            case Tokens.Token.Type.JOURNAL_ITEM_UNLOCKED:
                properties["itemid"] = ((Tokens.JournalItemUnlockedToken)token).itemId;
                break;
        }

        Rewards rewards = token.type switch
        {
            Tokens.Token.Type.LEVEL_UP => Rewards.fromDBRewardsModel(((Tokens.LevelUpToken)token).rewards).setLevel(((Tokens.LevelUpToken)token).level),
            _ => new Rewards(),
        };

        Token.Lifetime lifetime = token.type switch
        {
            Tokens.Token.Type.LEVEL_UP => Token.Lifetime.TRANSIENT,
            Tokens.Token.Type.JOURNAL_ITEM_UNLOCKED => Token.Lifetime.PERSISTENT,
            _ => throw new InvalidDataException($"Unknown Token type '{token.type}'"),
        };

        return new Token(
                Enum.Parse<Token.Type>(token.type.ToString()),
                properties,
                rewards.toApiResponse(),
                lifetime
        );
    }
}
