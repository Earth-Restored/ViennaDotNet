using System.Diagnostics;
using System.Text.Json;

namespace ViennaDotNet.StaticData;

public sealed class Levels
{
    public readonly Level[] levels;

    internal Levels(string dir)
    {
        try
        {
            LinkedList<Level> levels = [];
            string file;
            for (int levelIndex = 2; File.Exists(file = Path.Combine(dir, $"{levelIndex}.json")); levelIndex++)
            {
                using (var stream = File.OpenRead(file))
                {
                    var level = JsonSerializer.Deserialize<Level>(stream);

                    Debug.Assert(level is not null);

                    levels.AddLast(level);
                }
            }

            this.levels = [.. levels];

            for (int index = 1; index < this.levels.Length; index++)
            {
                if (this.levels[index].experienceRequired <= this.levels[index - 1].experienceRequired)
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
        int experienceRequired,
        int rubies,
        Level.Item[] items,
        string[] buildplates
    )
    {
        public sealed record Item(
            string id,
            int count
        );
    }
}
