namespace ViennaDotNet.DB.Models.Common;

public sealed record Rewards(
    int Rubies,
    int ExperiencePoints,
    int? Level,
    Dictionary<string, int?> Items,
    string[] Buildplates,
    string[] Challenges
);
