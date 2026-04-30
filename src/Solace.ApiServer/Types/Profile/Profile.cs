using ViennaDotNet.ApiServer.Types.Common;

namespace ViennaDotNet.ApiServer.Types.Profile;

public sealed record Profile(
    Dictionary<int, Profile.LevelR> LevelDistribution,
    int TotalExperience,
    int Level,
    int CurrentLevelExperience,
    int ExperienceRemaining,
    int Health,
    float HealthPercentage
)
{
    public sealed record LevelR(
        int ExperienceRequired,
        Rewards Rewards
    );
}
