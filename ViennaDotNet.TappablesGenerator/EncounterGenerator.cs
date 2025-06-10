using Serilog;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.TappablesGenerator;

public class EncounterGenerator
{
    // TODO: make these configurable
    private static readonly int CHANCE_PER_TILE = 4;
    private static readonly long MIN_DELAY = 1 * 60 * 1000;
    private static readonly long MAX_DELAY = 2 * 60 * 1000;

    private readonly StaticData.StaticData _staticData;
    private readonly int _maxDuration;

    private readonly Random _random;

    public EncounterGenerator(StaticData.StaticData staticData)
    {
        _staticData = staticData;

        if (_staticData.encountersConfig.encounters.Length == 0)
        {
            Log.Warning("No encounter configs provided");
        }
        _maxDuration = _staticData.encountersConfig.encounters.Select(encounterConfig => encounterConfig.duration).DefaultIfEmpty().Max() * 1000;

        _random = new Random();
    }

    public long getMaxEncounterLifetime()
    {
        return MAX_DELAY + this._maxDuration + 30 * 1000;
    }

    public Encounter[] generateEncounters(int tileX, int tileY, long currentTime)
    {
        if (_staticData.encountersConfig.encounters.Length == 0)
        {
            return [];
        }

        List<Encounter> encounters = [];
        if (_random.Next(0, CHANCE_PER_TILE) == 0)
        {
            long spawnDelay = _random.NextInt64(MIN_DELAY, MAX_DELAY + 1);

            EncountersConfig.EncounterConfig encounterConfig = _staticData.encountersConfig.encounters[_random.Next(0, _staticData.encountersConfig.encounters.Length)];

            float[] tileBounds = getTileBounds(tileX, tileY);
            float lat = _random.NextSingle(tileBounds[1], tileBounds[0]);
            float lon = _random.NextSingle(tileBounds[2], tileBounds[3]);

            Encounter encounter = new Encounter(
                    U.RandomUuid().ToString(),
                    lat,
                    lon,
                    currentTime + spawnDelay,
                    encounterConfig.duration * 1000,
                    encounterConfig.icon,
                    Enum.Parse<Encounter.Rarity>(encounterConfig.rarity.ToString()),
                    encounterConfig.encounterBuildplateId
            );
            encounters.Add(encounter);
        }

        return [.. encounters];
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
        return ((x * 2.0f - 1.0f) * float.Pi) * (180f / float.Pi);
    }

    private static float yToLat(float y)
    {
        return (float.Atan(float.Sinh((1.0f - y * 2.0f) * float.Pi))) * (180f / float.Pi);
    }
}