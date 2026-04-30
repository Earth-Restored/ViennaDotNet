using Serilog;
using System.Diagnostics;
using System.Text.Json.Serialization;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.ApiServer.Utils;

public sealed class TappablesManager
{
    private static readonly long GRACE_PERIOD = 30000;

    private Subscriber _subscriber = null!;
    private RequestSender _requestSender = null!;

    private readonly Dictionary<string, Dictionary<string, Tappable>> _tappables = [];
    private readonly Dictionary<string, Dictionary<string, Encounter>> _encounters = [];
    private int _pruneCounter = 0;

    private TappablesManager()
    {
    }

    public static async Task<TappablesManager> CreateAsync(EventBusClient eventBusClient)
    {
        var tappablesManager = new TappablesManager();

        tappablesManager._subscriber = await eventBusClient.AddSubscriberAsync("tappables", new SubscriberListener(
            tappablesManager.HandleEvent,
            async () =>
            {
                Log.Fatal("Tappables event bus subscriber error");
                Log.CloseAndFlush();
                Environment.Exit(1);
            }));
        tappablesManager._requestSender = await eventBusClient.AddRequestSenderAsync();

        return tappablesManager;
    }

    public Tappable[] GetTappablesAround(double lat, double lon, double radius)
        => [.. GetTileIdsAround(lat, lon, radius)
            .Select(tileId => _tappables.GetOrDefault(tileId, null))
            .Where(tappables => tappables is not null)
            .Select(items => items!.Values)
            .SelectMany(stream => stream)
            .Where(tappable =>
            {
                double dx = LonToX(tappable.Lon) * (1 << 16) - LonToX(lon) * (1 << 16);
                double dy = LatToY(tappable.Lat) * (1 << 16) - LatToY(lat) * (1 << 16);
                double distanceSquared = dx * dx + dy * dy;
                return distanceSquared <= radius * radius;
            })];

    public Encounter[] GetEncountersAround(double lat, double lon, double radius)
        => [.. GetTileIdsAround(lat, lon, radius)
            .Select(tileId => _encounters.GetOrDefault(tileId))
            .Where(encounters => encounters is not null)
            .SelectMany(encounters => encounters!.Values)
            .Where(encounter =>
            {
                double dx = LonToX(encounter.Lon) * (1 << 16) - LonToX(lon) * (1 << 16);
                double dy = LatToY(encounter.Lat) * (1 << 16) - LatToY(lat) * (1 << 16);
                double distanceSquared = dx * dx + dy * dy;
                return distanceSquared <= radius * radius;
            })];

    public Encounter[] GetEncountersAround(float lat, float lon, float radius)
        => [.. GetTileIdsAround(lat, lon, radius)
            .Select(tileId => _encounters.GetValueOrDefault(tileId))
            .Where(encounters => encounters is not null)
            .Select(encounters => encounters!.Values)
            .SelectMany(encounters => encounters)
            .Where(encounter =>
            {
                double dx = LonToX(encounter.Lon) * (1 << 16) - LonToX(lon) * (1 << 16);
                double dy = LatToY(encounter.Lat) * (1 << 16) - LatToY(lat) * (1 << 16);
                double distanceSquared = dx * dx + dy * dy;
                return distanceSquared <= radius * radius;
            })];

    private static string[] GetTileIdsAround(double lat, double lon, double radius)
    {
        int tileX = XToTile(LonToX(lon));
        int tileY = YToTile(LatToY(lat));
        int tileRadius = (int)Math.Ceiling(radius);
        return [.. Java.IntStream.Range(tileX - tileRadius, tileX + tileRadius + 1).Select(x => Java.IntStream.Range(tileY - tileRadius, tileY + tileRadius + 1).Select(y => $"{x}_{y}")).SelectMany(stream => stream)];
    }

    public Tappable? GetTappableWithId(string id, string tileId)
    {
        Dictionary<string, Tappable>? tappablesInTile = _tappables.GetOrDefault(tileId, null);
        if (tappablesInTile is not null)
        {
            Tappable? tappable = tappablesInTile.GetOrDefault(id, null);
            if (tappable is not null)
                return tappable;
        }

        return null;
    }

    public Encounter? GetEncounterWithId(string id, string tileId)
    {
        var encountersInTile = _encounters.GetOrDefault(tileId);
        if (encountersInTile is not null)
        {
            var encounter = encountersInTile.GetOrDefault(id);
            if (encounter is not null)
            {
                return encounter;
            }
        }

        return null;
    }

    public bool IsTappableValidFor(Tappable tappable, long requestTime, float lat, float lon)
    {
        if (tappable.SpawnTime - GRACE_PERIOD > requestTime || tappable.SpawnTime + tappable.ValidFor + GRACE_PERIOD <= requestTime)
        {
            return false;
        }

        // TODO: check player location is in radius, account for boosts

        return true;
    }

    // TODO: actually use this
    public bool IsEncounterValidFor(Encounter encounter, long requestTime, float lat, float lon)
    {
        if (encounter.SpawnTime - GRACE_PERIOD > requestTime || encounter.SpawnTime + encounter.ValidFor <= requestTime) // no grace period when checking end time because the buildplate instance shutdown does not include the grace period anyway
        {
            return false;
        }

        // TODO: check player location is in radius, account for boosts

        return true;
    }

    public async Task NotifyTileActiveAsync(string playerId, double lat, double lon)
    {
        int tileX = XToTile(LonToX(lon));
        int tileY = YToTile(LatToY(lat));
        string? response = await _requestSender.RequestAsync("tappables", "activeTile", Json.Serialize(new ActiveTileNotification(tileX, tileY, playerId)));
        if (response is null)
        {
            Log.Warning("Active tile notification event was rejected/ignored");
        }
    }

    private sealed record ActiveTileNotification(
        int X,
        int Y,
        string PlayerId
    );

    private Task HandleEvent(SubscriberEvent @event)
    {
        switch (@event.Type)
        {
            case "tappableSpawn":
                {
                    Tappable[]? tappables;
                    try
                    {
                        tappables = Json.Deserialize<Tappable[]>(@event.Data);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Could not deserialise tappable spawn event {ex}");
                        break;
                    }

                    Debug.Assert(tappables is not null);

                    foreach (var tappable in tappables)
                    {
                        AddTappable(tappable);
                    }

                    if (_pruneCounter++ == 10)
                    {
                        _pruneCounter = 0;
                        Prune(@event.Timestamp);
                    }
                }

                break;
            case "encounterSpawn":
                {
                    Encounter[]? encounters;

                    try
                    {
                        encounters = Json.Deserialize<Encounter[]>(@event.Data);
                    }
                    catch (Exception exception)
                    {
                        Log.Error($"Could not deserialise encounter spawn event: {exception}");
                        break;
                    }

                    Debug.Assert(encounters is not null);

                    foreach (var encounter in encounters)
                    {
                        AddEncounter(encounter);
                    }

                    if (_pruneCounter++ == 10)
                    {
                        _pruneCounter = 0;
                        Prune(@event.Timestamp);
                    }
                }

                break;
        }

        return Task.CompletedTask;
    }

    private void AddTappable(Tappable tappable)
    {
        string tileId = LocationToTileId(tappable.Lat, tappable.Lon);
        _tappables.ComputeIfAbsent(tileId, tileId1 => [])![tappable.Id] = tappable;
    }

    private void AddEncounter(Encounter encounter)
    {
        string tileId = LocationToTileId(encounter.Lat, encounter.Lon);
        _encounters.ComputeIfAbsent(tileId, tileId1 => [])![encounter.Id] = encounter;
    }

    private void Prune(long currentTime)
    {
        foreach (var tileTappables in _tappables.Values)
        {
            tileTappables.RemoveIf(entry =>
            {
                Tappable tappable = entry.Value;
                long expiresAt = tappable.SpawnTime + tappable.ValidFor;
                return expiresAt + GRACE_PERIOD <= currentTime;
            });
        }

        _tappables.RemoveIf(entry => entry.Value.IsEmpty());

        foreach (var tileEncounters in _encounters.Values)
        {
            tileEncounters.RemoveIf(entry =>
            {
                Encounter encounter = entry.Value;
                long expiresAt = encounter.SpawnTime + encounter.ValidFor;
                return expiresAt + GRACE_PERIOD <= currentTime;
            });
        }

        _encounters.RemoveIf(entry => entry.Value.Count == 0);
    }

    public static string LocationToTileId(float lat, float lon)
        => $"{XToTile(LonToX(lon))}_{YToTile(LatToY(lat))}";

    private static double LonToX(double lon)
        => (1.0 + MathE.ToRadians(lon) / Math.PI) / 2.0;

    private static double LatToY(double lat)
        => (1.0 - Math.Log(Math.Tan(MathE.ToRadians(lat)) + 1.0 / Math.Cos(MathE.ToRadians(lat))) / Math.PI) / 2.0;

    private static int XToTile(double x)
        => (int)Math.Floor(x * (1 << 16));

    private static int YToTile(double y)
        => (int)Math.Floor(y * (1 << 16));

    public sealed record Tappable(
        string Id,
        float Lat,
        float Lon,
        long SpawnTime,
        long ValidFor,
        string Icon,
        Tappable.RarityE Rarity,
        Tappable.Item[] Items
    )
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum RarityE
        {
            COMMON,
            UNCOMMON,
            RARE,
            EPIC,
            LEGENDARY
        }

        public sealed record Item(
            string Id,
            int Count
        );
    }

    public sealed record Encounter(
        string Id,
        float Lat,
        float Lon,
        long SpawnTime,
        long ValidFor,
        string Icon,
        Encounter.RarityE Rarity,
        string EncounterBuildplateId
    )
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum RarityE
        {
            COMMON,
            UNCOMMON,
            RARE,
            EPIC,
            LEGENDARY
        }
    }
}
