using Newtonsoft.Json;
using Serilog;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.TappablesGenerator;

public class Spawner
{
    private static readonly long SPAWN_INTERVAL = 15 * 1000;

    private readonly ActiveTiles activeTiles;
    private readonly TappableGenerator tappableGenerator;
    private readonly EncounterGenerator encounterGenerator;
    private readonly Publisher publisher;

    private readonly int maxTappableLifetimeIntervals;

    private long spawnCycleTime;
    private int spawnCycleIndex;
    private readonly Dictionary<int, int> lastSpawnCycleForTile = [];

    public Spawner(EventBusClient eventBusClient, ActiveTiles activeTiles, TappableGenerator tappableGenerator, EncounterGenerator encounterGenerator)
    {
        this.activeTiles = activeTiles;

        this.tappableGenerator = tappableGenerator;
        this.encounterGenerator = encounterGenerator;
        this.publisher = eventBusClient.addPublisher();

        this.maxTappableLifetimeIntervals = (int)(long.Max(this.tappableGenerator.getMaxTappableLifetime(), this.encounterGenerator.getMaxEncounterLifetime()) / SPAWN_INTERVAL + 1);

        this.spawnCycleTime = U.CurrentTimeMillis();
        this.spawnCycleIndex = maxTappableLifetimeIntervals;
    }

    public void run()
    {
        long nextTime = U.CurrentTimeMillis() + SPAWN_INTERVAL;
        for (; ; )
        {
            try
            {
                Thread.Sleep(Math.Max(0, (int)(nextTime - U.CurrentTimeMillis())));
            }
            catch (ThreadInterruptedException)
            {
                Log.Information("Spawn thread was interrupted, exiting");
                break;
            }

            nextTime += SPAWN_INTERVAL;

            doSpawnCycle();
        }
    }

    [Obsolete("Use spawnTiles instead.")]
    public void spawnTile(int tileX, int tileY)
    {
        long spawnCycleTime = this.spawnCycleTime;
        int spawnCycleIndex = this.spawnCycleIndex;

        while (spawnCycleTime < U.CurrentTimeMillis())
        {
            spawnCycleTime += SPAWN_INTERVAL;
            spawnCycleIndex++;
        }

        List<Tappable> tappables = [];

        List<Encounter> encounters = [];
        this.doSpawnCyclesForTile(tileX, tileY, spawnCycleTime, spawnCycleIndex, tappables, encounters);

        long tappableCutoffTime = spawnCycleTime - SPAWN_INTERVAL;
        tappables.RemoveAll(tappable => tappable.spawnTime + tappable.validFor < tappableCutoffTime);
        encounters.RemoveAll(encounter => encounter.spawnTime + encounter.validFor < tappableCutoffTime);

        this.sendSpawnedTappables(tappables, encounters);
    }

    public void spawnTiles(IEnumerable<ActiveTiles.ActiveTile> activeTiles)
    {
        long spawnCycleTime = this.spawnCycleTime;
        int spawnCycleIndex = this.spawnCycleIndex;

        while (spawnCycleTime < U.CurrentTimeMillis())
        {
            spawnCycleTime += SPAWN_INTERVAL;
            spawnCycleIndex++;
        }

        List<Tappable> tappables = [];
        List<Encounter> encounters = [];
        foreach (ActiveTiles.ActiveTile activeTile in activeTiles)
        {
            doSpawnCyclesForTile(activeTile.tileX, activeTile.tileY, spawnCycleTime, spawnCycleIndex, tappables, encounters);
        }

        long tappableCutoffTime = spawnCycleTime - SPAWN_INTERVAL;
        tappables.RemoveAll(tappable => tappable.spawnTime + tappable.validFor < tappableCutoffTime);
        encounters.RemoveAll(encounter => encounter.spawnTime + encounter.validFor < tappableCutoffTime);

        this.sendSpawnedTappables(tappables, encounters);
    }

    private void doSpawnCycle()
    {
        ActiveTiles.ActiveTile[] activeTiles = this.activeTiles.getActiveTiles(spawnCycleTime);

        while (spawnCycleTime < U.CurrentTimeMillis())
        {
            spawnCycleTime += SPAWN_INTERVAL;
            spawnCycleIndex++;
        }

        List<Tappable> tappables = [];
        List<Encounter> encounters = [];
        foreach (ActiveTiles.ActiveTile activeTile in activeTiles)
        {
            doSpawnCyclesForTile(activeTile.tileX, activeTile.tileY, spawnCycleTime, spawnCycleIndex, tappables, encounters);
        }

        long tappableCutoffTime = spawnCycleTime - SPAWN_INTERVAL;

        tappables.RemoveAll(tappable => tappable.spawnTime + tappable.validFor < tappableCutoffTime);
        encounters.RemoveAll(encounter => encounter.spawnTime + encounter.validFor < tappableCutoffTime);

        this.sendSpawnedTappables(tappables, encounters);
    }

    private void doSpawnCyclesForTile(int tileX, int tileY, long spawnCycleTime, int spawnCycleIndex, List<Tappable> tappables, List<Encounter> encounters)
    {
        int lastSpawnCycle = lastSpawnCycleForTile.GetOrDefault((tileX << 16) + tileY, 0);
        int cyclesToSpawn = Math.Min(spawnCycleIndex - lastSpawnCycle, maxTappableLifetimeIntervals);
        for (int index = 0; index < cyclesToSpawn; index++)
        {
            spawnTappablesForTile(tileX, tileY, spawnCycleTime - SPAWN_INTERVAL * (cyclesToSpawn - index - 1), tappables, encounters);
        }

        lastSpawnCycleForTile[(tileX << 16) + tileY] = spawnCycleIndex;
    }

    private void spawnTappablesForTile(int tileX, int tileY, long currentTime, List<Tappable> tappables, List<Encounter> encounters)
    {
        tappables.AddRange(tappableGenerator.generateTappables(tileX, tileY, currentTime));
        encounters.AddRange(encounterGenerator.generateEncounters(tileX, tileY, currentTime));
    }

    private void sendSpawnedTappables(List<Tappable> tappables, List<Encounter> encounters)
    {
        if (!publisher.publish("tappables", "tappableSpawn", JsonConvert.SerializeObject(tappables)).Result)
        {
            Log.Error("Event bus server rejected tappable spawn event");
        }

        if (!publisher.publish("tappables", "encounterSpawn", JsonConvert.SerializeObject(encounters)).Result)
        {
            Log.Error("Event bus server rejected encounter spawn event");
        }
    }
}
