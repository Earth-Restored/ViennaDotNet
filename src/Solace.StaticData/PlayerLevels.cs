using System.Collections.Immutable;
using System.Diagnostics;
using ViennaDotNet.Common;

namespace ViennaDotNet.StaticData;

public sealed class PlayerLevels
{
    public readonly ImmutableArray<Level> Levels;

    internal PlayerLevels(string dir)
    {
        try
        {
            LinkedList<Level> levels = [];
            string file;
            for (int levelIndex = 2; File.Exists(file = Path.Combine(dir, $"{levelIndex}.json")); levelIndex++)
            {
                using (var stream = File.OpenRead(file))
                {
                    var level = Json.Deserialize<Level>(stream);

                    Debug.Assert(level is not null);

                    levels.AddLast(level);
                }
            }

            Levels = [.. levels];

            for (int index = 1; index < Levels.Length; index++)
            {
                if (Levels[index].ExperienceRequired <= Levels[index - 1].ExperienceRequired)
                {
                    throw new StaticDataException($"Level {index + 2} has lower experience required than preceding level {index + 1}");
                }
            }
        }
        catch (StaticDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StaticDataException(null, exception);
        }
    }

    public sealed record Level(
        int ExperienceRequired,
        int Rubies,
        Level.Item[] Items,
        string[] Buildplates
    )
    {
        public sealed record Item(
            string Id,
            int Count
        );
    }
}
