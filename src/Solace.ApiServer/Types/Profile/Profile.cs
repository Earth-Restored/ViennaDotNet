using Solace.ApiServer.Types.Common;

namespace Solace.ApiServer.Types.Profile;

internal sealed record Profile(
    Dictionary<int, Profile.LevelR> LevelDistribution,
    int TotalExperience,
    int Level,
    int CurrentLevelExperience,
    int ExperienceRemaining,
    int Health,
    float HealthPercentage
)
{
    internal sealed record LevelR(
        int ExperienceRequired,
        Rewards Rewards
    );
}
