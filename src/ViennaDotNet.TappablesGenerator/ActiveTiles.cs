using Serilog;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.TappablesGenerator;

public sealed class ActiveTiles
{
    private static readonly int ACTIVE_TILE_RADIUS = 3;
    private static readonly long ACTIVE_TILE_EXPIRY_TIME = 2 * 60 * 1000;

    private readonly Dictionary<int, ActiveTile> _activeTiles = [];
    private readonly IActiveTileListener _activeTileListener;

    private ActiveTiles(IActiveTileListener activeTileListener)
    {
        _activeTileListener = activeTileListener;
    }

    public static async Task<ActiveTiles> CreateAsync(EventBusClient eventBusClient, IActiveTileListener activeTileListener)
    {
        var tiles = new ActiveTiles(activeTileListener);

        await eventBusClient.AddRequestHandlerAsync("tappables", new RequestHandlerLister(async request =>
        {
            if (request.Type == "activeTile")
            {
                ActiveTileNotification activeTileNotification;
                try
                {
                    activeTileNotification = Json.Deserialize<ActiveTileNotification>(request.Data)!;
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not deserialise active tile notification event: {ex}");
                    return null;
                }

                long currentTime = U.CurrentTimeMillis();
                tiles.PruneActiveTiles(currentTime);

                LinkedList<ActiveTile> newActiveTiles = [];
                for (int tileX = activeTileNotification.X - ACTIVE_TILE_RADIUS; tileX < activeTileNotification.X + ACTIVE_TILE_RADIUS + 1; tileX++)
                {
                    for (int tileY = activeTileNotification.Y - ACTIVE_TILE_RADIUS; tileY < activeTileNotification.Y + ACTIVE_TILE_RADIUS + 1; tileY++)
                    {
                        ActiveTile activeTile = tiles.MarkTileActive(tileX, tileY, currentTime);

                        if (activeTile.LatestActiveTime == activeTile.FirstActiveTime) // indicating that the tile is newly-active
                        {
                            newActiveTiles.AddLast(activeTile);
                        }
                    }
                }

                if (newActiveTiles.Count > 0)
                {
                    await activeTileListener.Active(newActiveTiles);
                }

                return string.Empty;
            }
            else
                return null;
        },
        async () =>
        {
            Log.Error("Event bus subscriber error");
            Log.CloseAndFlush();
            Environment.Exit(1);
        }));

        return tiles;
    }

    public IEnumerable<ActiveTile> GetActiveTiles(long currentTime)
        => _activeTiles.Values.Where(activeTile => currentTime < activeTile.LatestActiveTime + ACTIVE_TILE_EXPIRY_TIME);

    private ActiveTile MarkTileActive(int tileX, int tileY, long currentTime)
    {
        ActiveTile? activeTile = _activeTiles.GetOrDefault((tileX << 16) + tileY, null);
        if (activeTile is null)
        {
            Log.Information($"Tile {tileX},{tileY} is becoming active");
            activeTile = new ActiveTile(tileX, tileY, currentTime, currentTime);
        }
        else
        {
            activeTile = new ActiveTile(tileX, tileY, activeTile.FirstActiveTime, currentTime);
        }

        _activeTiles[(tileX << 16) + tileY] = activeTile;

        return activeTile;
    }

    private void PruneActiveTiles(long currentTime)
    {
        List<KeyValuePair<int, ActiveTile>> entriesToRemove = [];

        foreach (var item in _activeTiles)
        {
            ActiveTile activeTile = item.Value;
            if (activeTile.LatestActiveTime + ACTIVE_TILE_EXPIRY_TIME <= currentTime)
            {
                Log.Information($"Tile {activeTile.TileX},{activeTile.TileY} is inactive");
                entriesToRemove.Add(item);
            }
        }

        foreach (var item in entriesToRemove)
        {
            _activeTiles.Remove(item.Key);
        }

        _activeTileListener.Inactive(entriesToRemove.Select(item => item.Value));
    }

    public record ActiveTile(
        int TileX,
        int TileY,
        long FirstActiveTime,
        long LatestActiveTime
    );

    private sealed record ActiveTileNotification(
        int X,
        int Y,
        string PlayerId
    );

    public interface IActiveTileListener
    {
        Task Active(IEnumerable<ActiveTile> activeTiles);

        Task Inactive(IEnumerable<ActiveTile> activeTiles);
    }

    public class ActiveTileListener : IActiveTileListener
    {
        public Func<IEnumerable<ActiveTile>, Task>? OnActive;
        public Func<IEnumerable<ActiveTile>, Task>? OnInactive;

        public ActiveTileListener(Func<IEnumerable<ActiveTile>, Task>? _active, Func<IEnumerable<ActiveTile>, Task>? _inactive)
        {
            OnActive = _active;
            OnInactive = _inactive;
        }

        public Task Active(IEnumerable<ActiveTile> activeTiles)
            => OnActive?.Invoke(activeTiles) ?? Task.CompletedTask;

        public Task Inactive(IEnumerable<ActiveTile> activeTiles)
            => OnInactive?.Invoke(activeTiles) ?? Task.CompletedTask;
    }
}
