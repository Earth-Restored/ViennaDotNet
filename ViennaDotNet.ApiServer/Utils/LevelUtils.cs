using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;
using static ViennaDotNet.DB.Models.Player.Tokens;

namespace ViennaDotNet.ApiServer.Utils;

public sealed class LevelUtils
{
    public static EarthDB.Query checkAndHandlePlayerLevelUp(string playerId, long currentTime, StaticData.StaticData staticData)
    {
        EarthDB.Query getQuery = new EarthDB.Query(true);
        getQuery.Get("profile", playerId, typeof(Profile));
        getQuery.Then(results =>
        {
            Profile profile = (Profile)results.Get("profile").Value;
            EarthDB.Query updateQuery = new EarthDB.Query(true);
            bool changed = false;
            while (profile.level - 1 < staticData.levels.levels.Length && profile.experience >= staticData.levels.levels[profile.level - 1].experienceRequired)
            {
                changed = true;
                profile.level++;
                Rewards rewards = makeLevelRewards(staticData.levels.levels[profile.level - 2]);
                updateQuery.Then(TokenUtils.addToken(playerId, new LevelUpToken(profile.level, rewards.toDBRewardsModel())), false);
            }

            if (changed)
                updateQuery.Update("profile", playerId, profile);

            return updateQuery;
        });

        return getQuery;
    }

    public static Rewards makeLevelRewards(Levels.Level level)
    {
        Rewards rewards = new Rewards();
        if (level.rubies > 0)
        {
            rewards.addRubies(level.rubies);
        }

        foreach (var item in level.items)
        {
            rewards.addItem(item.id, item.count);
        }

        foreach (string buildplate in level.buildplates)
        {
            rewards.addBuildplate(buildplate);
        }

        return rewards;
    }
}
