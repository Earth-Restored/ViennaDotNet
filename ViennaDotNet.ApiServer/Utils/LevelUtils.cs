using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using static ViennaDotNet.DB.Models.Player.Tokens;

namespace ViennaDotNet.ApiServer.Utils
{
    public sealed class LevelUtils
    {
        // TODO: load this from data file
        private static Level[] levels = new Level[]{
            new Level(500, new Rewards().addRubies(15).addItem("730573d1-ba59-4fd4-89e0-85d4647466c2", 1).addItem("20dbd5fc-06b7-1aa1-5943-7ddaa2061e6a", 8).addItem("1eaa0d8c-2d89-2b84-aa1f-b75ccc85faff", 64))
    };

        public static Level[] getLevels()
        {
            return levels;
        }

        public static EarthDB.Query checkAndHandlePlayerLevelUp(string playerId, long currentTime, Catalog catalog)
        {
            EarthDB.Query getQuery = new EarthDB.Query(true);
            getQuery.Get("profile", playerId, typeof(Profile));
            getQuery.Then(results =>
            {
                Profile profile = (Profile)results.Get("profile").Value;
                EarthDB.Query updateQuery = new EarthDB.Query(true);
                bool changed = false;
                while (profile.level - 1 < levels.Length && profile.experience >= levels[profile.level - 1].experienceRequired)
                {
                    changed = true;
                    profile.level++;
                    Rewards rewards = levels[profile.level - 2].rewards;
                    updateQuery.Then(ActivityLogUtils.addEntry(playerId, new ActivityLog.LevelUpEntry(currentTime, profile.level)));
                    updateQuery.Then(rewards.toRedeemQuery(playerId, currentTime, catalog));
                    updateQuery.Then(TokenUtils.addToken(playerId, new LevelUpToken(profile.level)));
                }

                if (changed)
                    updateQuery.Update("profile", playerId, profile);

                return updateQuery;
            });

            return getQuery;
        }

        public record Level(
            int experienceRequired,
            Rewards rewards
        )
        {
        }
    }
}
