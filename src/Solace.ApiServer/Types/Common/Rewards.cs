namespace ViennaDotNet.ApiServer.Types.Common;

// TODO: determine format
public sealed record Rewards(
    int? Rubies,
    int? ExperiencePoints,
    int? Level,
    Rewards.Item[] Inventory,
    string[] Buildplates,
    Rewards.Challenge[] Challenges,
    string[] PersonaItems,
    Rewards.UtilityBlock[] UtilityBlocks
)
{
    public sealed record Item(
        string Id,
        int Amount
    );

    public sealed record Challenge(
        string Id
    );

    public sealed record UtilityBlock();
}
