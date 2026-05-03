using Serilog;
using Solace.Common;
using Solace.Common.Utils;
using Solace.StaticData;

namespace Solace.TappablesGenerator;

public class TappableGenerator
{
    // TODO: make these configurable
    private static readonly int MIN_COUNT = 1;
    private static readonly int MAX_COUNT = 3;
    private static readonly long MIN_DURATION = 2 * 60 * 1000;
    private static readonly long MAX_DURATION = 5 * 60 * 1000;
    private static readonly long MIN_DELAY = 1 * 60 * 1000;
    private static readonly long MAX_DELAY = 2 * 60 * 1000;

    private readonly StaticData.StaticData _staticData;

    private readonly Random _random;

    public TappableGenerator(StaticData.StaticData staticData)
    {
        _staticData = staticData;

        if (_staticData.TappablesConfig.Tappables.Length == 0)
        {
            Log.Warning("No tappable configs provided");
        }

        _random = new Random();
    }

    public static long GetMaxTappableLifetime()
        => MAX_DELAY + MAX_DURATION + 30 * 1000;

    public Tappable[] GenerateTappables(int tileX, int tileY, long currentTime)
    {
        if (_staticData.TappablesConfig.Tappables.Length == 0)
        {
            return [];
        }

        LinkedList<Tappable> tappables = new();
        Span<float> tileBounds = stackalloc float[4];
        for (int count = _random.Next(MIN_COUNT, MAX_COUNT + 1); count > 0; count--)
        {
            long spawnDelay = _random.NextInt64(MIN_DELAY, MAX_DELAY + 1);
            long duration = _random.NextInt64(MIN_DURATION, MAX_DURATION + 1);

            TappablesConfig.TappableConfig tappableConfig = _staticData.TappablesConfig.Tappables[_random.Next(0, _staticData.TappablesConfig.Tappables.Length)];

            GetTileBounds(tileX, tileY, tileBounds);
            float lat = _random.NextSingle(tileBounds[1], tileBounds[0]);
            float lon = _random.NextSingle(tileBounds[2], tileBounds[3]);

            int dropSetIndex = _random.Next(0, tappableConfig.DropSets.Select(dropSet => dropSet.Chance).Sum());
            TappablesConfig.TappableConfig.DropSetR? dropSet = null;

            foreach (TappablesConfig.TappableConfig.DropSetR dropSet1 in tappableConfig.DropSets)
            {
                dropSet = dropSet1;
                dropSetIndex -= dropSet1.Chance;
                if (dropSetIndex <= 0)
                {
                    break;
                }
            }

            if (dropSet is null)
            {
                throw new InvalidOperationException();
            }

            LinkedList<Tappable.Item> items = new();

            foreach (string itemId in dropSet.Items)
            {
                TappablesConfig.TappableConfig.ItemCount itemCount = tappableConfig.ItemCounts[itemId];
                items.AddLast(new Tappable.Item(itemId, _random.Next(itemCount.Min, itemCount.Max + 1)));
            }

            Tappable.RarityE rarity = Enum.Parse<Tappable.RarityE>(items.Select(item => _staticData.Catalog.ItemsCatalog.GetItem(item.Id)!.Rarity).Max().ToString());

            var tappable = new Tappable(
                U.RandomUuid().ToString(),
                lat,
                lon,
                currentTime + spawnDelay,
                duration,
                tappableConfig.Icon,
                rarity,
                [.. items]
            );
            tappables.AddLast(tappable);
        }

        return [.. tappables];
    }

    private static void GetTileBounds(int tileX, int tileY, Span<float> dest)
    {
        dest[0] = YToLat((float)tileY / (1 << 16));
        dest[1] = YToLat((float)(tileY + 1) / (1 << 16));
        dest[2] = XToLon((float)tileX / (1 << 16));
        dest[3] = XToLon((float)(tileX + 1) / (1 << 16));
    }

    private static float XToLon(float x)
        => (float)MathE.ToDegrees((x * 2.0d - 1.0d) * double.Pi);

    private static float YToLat(float y)
        => (float)MathE.ToDegrees(double.Atan(double.Sinh((1.0d - y * 2.0d) * double.Pi)));
}