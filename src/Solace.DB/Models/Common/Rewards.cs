namespace Solace.DB.Models.Common;

// todo: implement gethashcode and equals
public sealed record Rewards(
    int Rubies,
    int ExperiencePoints,
    int? Level,
    Dictionary<string, int?> Items,
    string[] Buildplates,
    string[] Challenges
);
