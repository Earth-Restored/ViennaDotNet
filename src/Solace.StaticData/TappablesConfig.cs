using System.Collections.Immutable;
using System.Diagnostics;
using ViennaDotNet.Common;

namespace ViennaDotNet.StaticData;

public sealed class TappablesConfig
{
    public readonly ImmutableArray<TappableConfig> Tappables;

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
                    var tappable = Json.Deserialize<TappableConfig>(stream);

                    Debug.Assert(tappable is not null);

                    tappables.AddLast(tappable);
                }
            }

            Tappables = [.. tappables];

            foreach (TappableConfig tappableConfig in Tappables)

            {
                foreach (TappableConfig.DropSetR dropSet in tappableConfig.DropSets)
                {
                    foreach (string itemId in dropSet.Items)
                    {
                        if (!tappableConfig.ItemCounts.ContainsKey(itemId))
                        {
                            throw new StaticDataException($"Tappable config {tappableConfig.Icon} has no item count for item {itemId}");
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
        string Icon,
        TappableConfig.DropSetR[] DropSets,
        Dictionary<string, TappableConfig.ItemCount> ItemCounts
    )
    {
        public record DropSetR(
            string[] Items,
            int Chance
        );

        public record ItemCount(
            int Min,
            int Max
        );
    }
}
