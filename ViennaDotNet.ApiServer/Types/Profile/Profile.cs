using ViennaDotNet.ApiServer.Types.Common;

namespace ViennaDotNet.ApiServer.Types.Profile
{
    public record Profile(
        Dictionary<int, Profile.Level> levelDistribution,
        int totalExperience,
        int level,
        int currentLevelExperience,
        int experienceRemaining,
        int health,
        float healthPercentage
    )
    {
        public record Level(
            int experienceRequired,
            Rewards rewards
        )
        {
        }
    }
}
