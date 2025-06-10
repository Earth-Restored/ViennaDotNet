using Serilog;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.TappablesGenerator;

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

        if (_staticData.tappablesConfig.tappables.Length == 0)
        {
            Log.Warning("No tappable configs provided");
        }

        _random = new Random();
    }

    public long getMaxTappableLifetime()
    {
        return MAX_DELAY + MAX_DURATION + 30 * 1000;
    }

    public Tappable[] generateTappables(int tileX, int tileY, long currentTime)
    {
        if (_staticData.tappablesConfig.tappables.Length == 0)
        {
            return [];
        }

        LinkedList<Tappable> tappables = new();
        for (int count = _random.Next(MIN_COUNT, MAX_COUNT + 1); count > 0; count--)
        {
            long spawnDelay = _random.NextInt64(MIN_DELAY, MAX_DELAY + 1);
            long duration = _random.NextInt64(MIN_DURATION, MAX_DURATION + 1);

            TappablesConfig.TappableConfig tappableConfig = _staticData.tappablesConfig.tappables[_random.Next(0, _staticData.tappablesConfig.tappables.Length)];

            float[] tileBounds = getTileBounds(tileX, tileY);
            float lat = _random.NextSingle(tileBounds[1], tileBounds[0]);
            float lon = _random.NextSingle(tileBounds[2], tileBounds[3]);

            int dropSetIndex = _random.Next(0, tappableConfig.dropSets.Select(dropSet => dropSet.chance).Sum());
            TappablesConfig.TappableConfig.DropSet? dropSet = null;

            foreach (TappablesConfig.TappableConfig.DropSet dropSet1 in tappableConfig.dropSets)
            {
                dropSet = dropSet1;
                dropSetIndex -= dropSet1.chance;
                if (dropSetIndex <= 0)
                {
                    break;
                }
            }

            if (dropSet == null)
            {
                throw new InvalidOperationException();
            }

            LinkedList<Tappable.Drops.Item> items = new();

            foreach (string itemId in dropSet.items)
            {
                TappablesConfig.TappableConfig.ItemCount itemCount = tappableConfig.itemCounts[itemId];
                items.AddLast(new Tappable.Drops.Item(itemId, _random.Next(itemCount.min, itemCount.max + 1)));
            }

            int experiencePoints = items.Select(item => _staticData.catalog.itemsCatalog.getItem(item.id)!.experience.tappable * item.count).Sum();
            Tappable.Rarity rarity = Enum.Parse<Tappable.Rarity>(items.Select(item => _staticData.catalog.itemsCatalog.getItem(item.id)!.rarity).Max().ToString());

            Tappable.Drops drops = new Tappable.Drops(
                experiencePoints,
                [.. items]
            );

            Tappable tappable = new Tappable(
                U.RandomUuid().ToString(),
                lat,
                lon,
                currentTime + spawnDelay,
                duration,
                tappableConfig.icon,
                rarity,
                drops
            );
            tappables.AddLast(tappable);
        }

        return [.. tappables];
    }

    private static float[] getTileBounds(int tileX, int tileY)
    {
        return [
            yToLat((float) tileY / (1 << 16)),
            yToLat((float) (tileY + 1) / (1 << 16)),
            xToLon((float) tileX / (1 << 16)),
            xToLon((float) (tileX + 1) / (1 << 16))
    ];
    }

    private static float xToLon(float x)
    {
        return (float)MathE.ToDegrees((x * 2.0 - 1.0) * Math.PI);
    }

    private static float yToLat(float y)
    {
        return (float)MathE.ToDegrees(Math.Atan(Math.Sinh((1.0 - y * 2.0) * Math.PI)));
    }
}