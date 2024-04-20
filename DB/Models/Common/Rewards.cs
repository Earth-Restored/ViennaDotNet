using System.Reflection.Emit;

namespace ViennaDotNet.DB.Models.Common
{
    public record Rewards(
        int rubies,
        int experiencePoints,
        int level,
        Dictionary<string, int?> items,
        string[] buildplates,
        string[] challenges
    )
    {
    }
}
