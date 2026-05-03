using Solace.Common.Utils;
using Solace.DB;
using Solace.DB.Models.Player;

namespace Solace.ApiServer.Utils;

public static class TokenUtils
{
    public static EarthDB.Query AddToken(string playerId, Tokens.Token token)
    {
        var getQuery = new EarthDB.Query(true);
        getQuery.Get("tokens", playerId, typeof(Tokens));
        getQuery.Then(results =>
        {
            Tokens tokens = results.Get<Tokens>("tokens");
            string id = U.RandomUuid().ToString();
            tokens.AddToken(id, token);
            var updateQuery = new EarthDB.Query(true);
            updateQuery.Update("tokens", playerId, tokens);
            updateQuery.Extra("tokenId", id);
            return updateQuery;
        });
        return getQuery;
    }

    // does not handle redeeming the token itself (removing it from the list of tokens belonging to the player)
    public static EarthDB.Query DoActionsOnRedeemedToken(Tokens.Token token, string playerId, long currentTime, StaticData.StaticData staticData)
    {
        var getQuery = new EarthDB.Query(true);

        switch (token.Type)
        {
            case Tokens.Token.TypeE.LEVEL_UP:
                {
                    var levelUpToken = (Tokens.LevelUpToken)token;

                    getQuery.Then(results =>
                    {
                        var updateQuery = new EarthDB.Query(true);

                        updateQuery.Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.LevelUpEntry(currentTime, levelUpToken.Level)));

                        updateQuery.Then(Rewards.FromDBRewardsModel(levelUpToken.Rewards).ToRedeemQuery(playerId, currentTime, staticData));

                        return updateQuery;
                    }, false);
                }

                break;
            case Tokens.Token.TypeE.JOURNAL_ITEM_UNLOCKED:
                {
                    var journalItemUnlockedToken = (Tokens.JournalItemUnlockedToken)token;
                    getQuery.Then(results =>
                    {
                        var updateQuery = new EarthDB.Query(true);

                        updateQuery.Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.JournalItemUnlockedEntry(currentTime, journalItemUnlockedToken.ItemId)));

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
