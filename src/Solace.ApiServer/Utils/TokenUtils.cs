using Microsoft.EntityFrameworkCore;
using Solace.Common.Utils;
using Solace.DB;
using Solace.DB.Models.Player;
using Solace.DB.Utils;

namespace Solace.ApiServer.Utils;

public static class TokenUtils
{
    public static async Task<string> AddTokenAsync(EarthDbContext.Results results, Guid accountId, TokensEF.Token token)
    {
        var tokens = await results.EarthDb.Tokens
            .AsTracking()
            .FirstOrNewAsync(tokens => tokens.Id == accountId);

        string id = Guid.NewGuid().ToString();
        tokens.AddToken(id, token);

        await results.EarthDb.SaveChangesAsync();

        results.Tokens = tokens.Version;

        return id;
    }

    // does not handle redeeming the token itself (removing it from the list of tokens belonging to the player)
    public static async Task<TokensEF.Token> DoActionsOnRedeemedTokenAsync(EarthDbContext.Results results, TokensEF.Token token, Guid accountId, long currentTime, StaticData.StaticData staticData)
    {
        switch (token)
        {
            case TokensEF.LevelUpToken levelUpToken:
                {
                    await ActivityLogUtils.AddEntryAsync(results, accountId, new ActivityLogEF.LevelUpEntry(currentTime, levelUpToken.Level));

                    await Rewards.FromDBRewardsModel(levelUpToken.Rewards).ToRedeemQueryAsync(results, accountId, currentTime, staticData);
                }

                break;
            case TokensEF.JournalItemUnlockedToken journalItemUnlockedToken:
                {
                    await ActivityLogUtils.AddEntryAsync(results, accountId, new ActivityLogEF.JournalItemUnlockedEntry(currentTime, journalItemUnlockedToken.ItemId));

                    /*int experiencePoints = staticData.catalog.itemsCatalog.getItem(journalItemUnlockedToken.itemId).experience().journal();
                    if (experiencePoints > 0)
                    {
                        updateQuery.then(new Rewards().addExperiencePoints(experiencePoints).toRedeemQuery(playerId, currentTime, staticData));
                    }*/
                }

                break;
        }

        return token;
    }
}
