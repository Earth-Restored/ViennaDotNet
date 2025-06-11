using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Utils;

public static class TokenUtils
{
    public static EarthDB.Query addToken(string playerId, Tokens.Token token)
    {
        EarthDB.Query getQuery = new EarthDB.Query(true);
        getQuery.Get("tokens", playerId, typeof(Tokens));
        getQuery.Then(results =>
        {
            Tokens tokens = (Tokens)results.Get("tokens").Value;
            string id = U.RandomUuid().ToString();
            tokens.addToken(id, token);
            EarthDB.Query updateQuery = new EarthDB.Query(true);
            updateQuery.Update("tokens", playerId, tokens);
            updateQuery.Extra("tokenId", id);
            return updateQuery;
        });
        return getQuery;
    }

    // does not handle redeeming the token itself (removing it from the list of tokens belonging to the player)
    public static EarthDB.Query doActionsOnRedeemedToken(Tokens.Token token, string playerId, long currentTime, StaticData.StaticData staticData)
    {
        EarthDB.Query getQuery = new EarthDB.Query(true);

        switch (token.type)
        {
            case Tokens.Token.Type.LEVEL_UP:
                {
                    Tokens.LevelUpToken levelUpToken = (Tokens.LevelUpToken)token;

                    getQuery.Then(results =>
                    {
                        EarthDB.Query updateQuery = new EarthDB.Query(true);

                        updateQuery.Then(ActivityLogUtils.addEntry(playerId, new ActivityLog.LevelUpEntry(currentTime, levelUpToken.level)));

                        updateQuery.Then(Rewards.fromDBRewardsModel(levelUpToken.rewards).toRedeemQuery(playerId, currentTime, staticData));

                        return updateQuery;
                    }, false);
                }

                break;
            case Tokens.Token.Type.JOURNAL_ITEM_UNLOCKED:
                {
                    Tokens.JournalItemUnlockedToken journalItemUnlockedToken = (Tokens.JournalItemUnlockedToken)token;
                    getQuery.Then(results =>
                    {
                        EarthDB.Query updateQuery = new EarthDB.Query(true);

                        updateQuery.Then(ActivityLogUtils.addEntry(playerId, new ActivityLog.JournalItemUnlockedEntry(currentTime, journalItemUnlockedToken.itemId)));

                        /*int experiencePoints = staticData.catalog.itemsCatalog.getItem(journalItemUnlockedToken.itemId).experience().journal();
                        if (experiencePoints > 0)
                        {
                            updateQuery.then(new Rewards().addExperiencePoints(experiencePoints).toRedeemQuery(playerId, currentTime, staticData));
                        }*/

                        return updateQuery;
                    }, false);
                }

                break;
        }

        getQuery.Extra("token", token);

        return getQuery;
    }
}
