using Newtonsoft.Json;
using Serilog;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.TappablesGenerator;

public sealed class ActiveTiles
{
    private static readonly int ACTIVE_TILE_RADIUS = 3;
    private static readonly long ACTIVE_TILE_EXPIRY_TIME = 2 * 60 * 1000;

    private readonly Dictionary<int, ActiveTile> activeTiles = [];
    private readonly IActiveTileListener activeTileListener;

    public ActiveTiles(EventBusClient eventBusClient, IActiveTileListener activeTileListener)
    {
        this.activeTileListener = activeTileListener;

        eventBusClient.addRequestHandler("tappables", new RequestHandler.Handler(request =>
        {
            if (request.type == "activeTile")
            {
                ActiveTileNotification activeTileNotification;
                try
                {
                    activeTileNotification = JsonConvert.DeserializeObject<ActiveTileNotification>(request.data)!;
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not deserialise active tile notification event: {ex}");
                    return null;
                }

                long currentTime = U.CurrentTimeMillis();
                pruneActiveTiles(currentTime);

                LinkedList<ActiveTile> newActiveTiles = [];
                for (int tileX = activeTileNotification.x - ACTIVE_TILE_RADIUS; tileX < activeTileNotification.x + ACTIVE_TILE_RADIUS + 1; tileX++)
                {
                    for (int tileY = activeTileNotification.y - ACTIVE_TILE_RADIUS; tileY < activeTileNotification.y + ACTIVE_TILE_RADIUS + 1; tileY++)
                    {
                        ActiveTile activeTile = markTileActive(tileX, tileY, currentTime);

                        if (activeTile.latestActiveTime == activeTile.firstActiveTime) // indicating that the tile is newly-active
                        {
                            newActiveTiles.AddLast(activeTile);
                        }
                    }
                }

                if (newActiveTiles.Count > 0)
                {
                    activeTileListener.active(newActiveTiles);
                }

                return string.Empty;
            }
            else
                return null;
        }, () =>
        {
            Log.Error("Event bus subscriber error");
            Log.CloseAndFlush();
            Environment.Exit(1);
        }));
    }

    public ActiveTile[] getActiveTiles(long currentTime)
    {
        return [.. activeTiles.Values.Where(activeTile => currentTime < activeTile.latestActiveTime + ACTIVE_TILE_EXPIRY_TIME)];
    }

    private ActiveTile markTileActive(int tileX, int tileY, long currentTime)
    {
        ActiveTile? activeTile = activeTiles.GetOrDefault((tileX << 16) + tileY, null);
        if (activeTile == null)
        {
            Log.Information($"Tile {tileX},{tileY} is becoming active");
            activeTile = new ActiveTile(tileX, tileY, currentTime, currentTime);
        }
        else
        {
            activeTile = new ActiveTile(tileX, tileY, activeTile.firstActiveTime, currentTime);
        }

        activeTiles[(tileX << 16) + tileY] = activeTile;

        return activeTile;
    }

    private void pruneActiveTiles(long currentTime)
    {
        List<KeyValuePair<int, ActiveTile>> entriesToRemove = [];

        foreach (var item in activeTiles)
        {
            ActiveTile activeTile = item.Value;
            if (activeTile.latestActiveTime + ACTIVE_TILE_EXPIRY_TIME <= currentTime)
            {
                Log.Information($"Tile {activeTile.tileX},{activeTile.tileY} is inactive");
                entriesToRemove.Add(item);
            }
        }

        foreach (var item in entriesToRemove)
        {
            activeTiles.Remove(item.Key);
        }

        activeTileListener.inactive(entriesToRemove.Select(item => item.Value));
    }

    public record ActiveTile(
        int tileX,
        int tileY,
        long firstActiveTime,
        long latestActiveTime
    );

    private sealed record ActiveTileNotification(
        int x,
        int y,
        string playerId
    );

    public interface IActiveTileListener
    {
        void active(IEnumerable<ActiveTile> activeTiles);

        void inactive(IEnumerable<ActiveTile> activeTiles);
    }

    public class ActiveTileListener : IActiveTileListener
    {
        public Action<IEnumerable<ActiveTile>>? Active;
        public Action<IEnumerable<ActiveTile>>? Inactive;

        public ActiveTileListener(Action<IEnumerable<ActiveTile>>? _active, Action<IEnumerable<ActiveTile>>? _inactive)
        {
            Active = _active;
            Inactive = _inactive;
        }

        public void active(IEnumerable<ActiveTile> activeTiles)
            => Active?.Invoke(activeTiles);

        public void inactive(IEnumerable<ActiveTile> activeTiles)
            => Inactive?.Invoke(activeTiles);
    }
}
