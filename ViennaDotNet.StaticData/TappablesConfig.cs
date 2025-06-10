using System.Diagnostics;
using System.Text.Json;

namespace ViennaDotNet.StaticData;

public sealed class TappablesConfig
{
    public readonly TappableConfig[] tappables;

    internal TappablesConfig(string dir)
    {
        try
        {
            LinkedList<TappableConfig> tappables = [];
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (Path.GetExtension(file) != ".json")
                {
                    continue;
                }

                using (var stream = File.OpenRead(file))
                {
                    var tappable = JsonSerializer.Deserialize<TappableConfig>(stream);

                    Debug.Assert(tappable is not null);

                    tappables.AddLast(tappable);
                }
            }

            this.tappables = [.. tappables];

            foreach (TappableConfig tappableConfig in this.tappables)

            {
                foreach (TappableConfig.DropSet dropSet in tappableConfig.dropSets)
                {
                    foreach (string itemId in dropSet.items)
                    {
                        if (!tappableConfig.itemCounts.ContainsKey(itemId))
                        {
                            throw new StaticDataException($"Tappable config {tappableConfig.icon} has no item count for item {itemId}");
                        }
                    }
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

    public record TappableConfig(
        string icon,
        TappableConfig.DropSet[] dropSets,
        Dictionary<string, TappableConfig.ItemCount> itemCounts
    )
    {
        public record DropSet(
            string[] items,
            int chance
        );

        public record ItemCount(
            int min,
            int max
        );
    }
}
