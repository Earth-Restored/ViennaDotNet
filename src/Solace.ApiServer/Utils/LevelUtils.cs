using Solace.DB;
using Solace.DB.Models.Player;
using Solace.StaticData;
using static Solace.DB.Models.Player.Tokens;

namespace Solace.ApiServer.Utils;

public sealed class LevelUtils
{
#pragma warning disable IDE0060 // Remove unused parameter
    public static EarthDB.Query CheckAndHandlePlayerLevelUp(string playerId, long currentTime, StaticData.StaticData staticData)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        var getQuery = new EarthDB.Query(true);
        getQuery.Get("profile", playerId, typeof(Profile));
        getQuery.Then(results =>
        {
            Profile profile = results.Get<Profile>("profile");
            var updateQuery = new EarthDB.Query(true);
            bool changed = false;
            while (profile.Level - 1 < staticData.Levels.Levels.Length && profile.Experience >= staticData.Levels.Levels[profile.Level - 1].ExperienceRequired)
            {
                changed = true;
                profile.Level++;
                Rewards rewards = MakeLevelRewards(staticData.Levels.Levels[profile.Level - 2]);
                updateQuery.Then(TokenUtils.AddToken(playerId, new LevelUpToken(profile.Level, rewards.ToDBRewardsModel())), false);
            }

            if (changed)
            {
                updateQuery.Update("profile", playerId, profile);
            }

            return updateQuery;
        });

        return getQuery;
    }

    public static Rewards MakeLevelRewards(PlayerLevels.Level level)
    {
        var rewards = new Rewards();
        if (level.Rubies > 0)
        {
            rewards.AddRubies(level.Rubies);
        }

        foreach (var item in level.Items)
        {
            rewards.AddItem(item.Id, item.Count);
        }

        foreach (string buildplate in level.Buildplates)
        {
            rewards.AddBuildplate(buildplate);
        }

        return rewards;
    }
}
