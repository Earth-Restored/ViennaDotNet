using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
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

        string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, Dictionary<string, Token>>()
        {
            {
                "tokens",
                tokens.GetTokens().Collect(() => new Dictionary<string, Token>(), (hashmap, token) =>
                {
                    hashmap[token.Id] = TokenToApiResponse(token.Token);
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
        long requestStartedOn = HttpContext.GetTimestamp();

        Tokens.Token? token;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("tokens", playerId, typeof(Tokens))
                .Then(results1 =>
                {
                    Tokens tokens = (Tokens)results1.Get("tokens").Value;
                    Tokens.Token? removedToken = tokens.RemoveToken(tokenId);
                    if (removedToken is not null)
                    {
                        return new EarthDB.Query(true)
                            .Update("tokens", playerId, tokens)
                            .Then(TokenUtils.DoActionsOnRedeemedToken(removedToken, playerId, requestStartedOn, staticData), false)
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
            token = (bool)results.GetExtra("success") ? (Tokens.Token)results.GetExtra("token") : null;
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        if (token is not null)
        {
            string resp = Json.Serialize(TokenToApiResponse(token));
            return Content(resp, "application/json");
        }
        else
        {
            return BadRequest();
        }
    }

    private static Token TokenToApiResponse(Tokens.Token token)
    {
        Dictionary<string, string> properties = [];
        switch (token.Type)
        {
            case Tokens.Token.TypeE.JOURNAL_ITEM_UNLOCKED:
                properties["itemid"] = ((Tokens.JournalItemUnlockedToken)token).ItemId;
                break;
        }

        Rewards rewards = token.Type switch
        {
            Tokens.Token.TypeE.LEVEL_UP => Rewards.FromDBRewardsModel(((Tokens.LevelUpToken)token).Rewards).setLevel(((Tokens.LevelUpToken)token).Level),
            _ => new Rewards(),
        };

        Token.LifetimeE lifetime = token.Type switch
        {
            Tokens.Token.TypeE.LEVEL_UP => Token.LifetimeE.TRANSIENT,
            Tokens.Token.TypeE.JOURNAL_ITEM_UNLOCKED => Token.LifetimeE.PERSISTENT,
            _ => throw new InvalidDataException($"Unknown Token type '{token.Type}'"),
        };

        return new Token(
                Enum.Parse<Token.Type>(token.Type.ToString()),
                properties,
                rewards.ToApiResponse(),
                lifetime
        );
    }
}
