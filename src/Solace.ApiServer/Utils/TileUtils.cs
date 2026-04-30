using Serilog;
using ViennaDotNet.Common;
using ViennaDotNet.DB;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.ApiServer.Utils;

internal static class TileUtils
{
    private static EarthDB db => Program.DB;

    private static RequestSender? _requestSender;

    public static async Task<bool> TryWriteTile(int tileX, int tileY, Stream dest, CancellationToken cancellationToken)
    {
        ulong dbPos = ToDbPos(tileX, tileY);

        var results = await new EarthDB.ObjectQuery(false)
            .GetTile(dbPos)
            .ExecuteAsync(db, cancellationToken);

        string? tileObjectId = results.GetTile(dbPos);

        await using var objectStoreClient = await Program.GetObjectStoreClient();

        if (!string.IsNullOrEmpty(tileObjectId))
        {
            return await TryWriteTileFromObject(tileObjectId, dest, objectStoreClient, cancellationToken);
        }

        Log.Information("Rendering tile");
        _requestSender ??= await Program.eventBus.AddRequestSenderAsync();
        string? tilePng64 = await _requestSender.RequestAsync("tile", "renderTile", Json.Serialize(new RenderTileRequest(tileX, tileY, 16)));

        if (tilePng64 is null)
        {
            Log.Warning("Could not get tile (tile renderer did not respond to event bus request)");
            return false;
        }

        byte[] tilePng = Convert.FromBase64String(tilePng64);

        tileObjectId = await objectStoreClient.StoreAsync(tilePng);

        if (string.IsNullOrEmpty(tileObjectId))
        {
            Log.Warning("Failed to store tile to object store");
            return false;
        }

        Log.Debug($"Stored tile ({tileX}, {tileY}) to object store under id {tileObjectId}");

        _ = await new EarthDB.ObjectQuery(true)
            .UpdateTile(dbPos, tileObjectId)
            .ExecuteAsync(db, cancellationToken);

        await dest.WriteAsync(tilePng, cancellationToken);

        return true;
    }

    private static async Task<bool> TryWriteTileFromObject(string tileObjectId, Stream dest, ObjectStoreClient objectStoreClient, CancellationToken cancellationToken)
    {
        byte[]? tilePng = await objectStoreClient.GetAsync(tileObjectId);

        if (tilePng is null)
        {
            return false;
        }

        await dest.WriteAsync(tilePng, cancellationToken);

        return true;
    }

    private static ulong ToDbPos(int tileX, int tileY)
        => unchecked((ulong)((long)tileX | ((long)tileY << 32)));

    private sealed record RenderTileRequest(int TileX, int TileY, int Zoom);
}
