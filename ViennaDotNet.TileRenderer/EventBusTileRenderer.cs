using Npgsql;
using Serilog;
using SkiaSharp;
using System.Text.Json;
using ViennaDotNet.Common;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.TileRenderer;

internal sealed class EventBusTileRenderer : IDisposable
{
    private readonly NpgsqlDataSource _tileDB;
    private readonly EventBusClient _eventBus;
    private readonly TileRenderer _renderer;

    public EventBusTileRenderer(NpgsqlDataSource tileDB, EventBusClient eventBus, StaticData.StaticData staticData)
    {
        _tileDB = tileDB;
        _eventBus = eventBus;
        _renderer = TileRenderer.Create(staticData.TileRenderer.TagMapJson, Log.Logger);
    }

    public void Run()
    {
        _eventBus.AddRequestHandler("tile", new RequestHandler.Handler(async request =>
        {
            if (request.Type == "renderTile")
            {
                RenderTileRequest getTile;
                try
                {
                    getTile = Json.Deserialize<RenderTileRequest>(request.Data)!;
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not deserialise renderTile request: {ex}");
                    return null;
                }

                Log.Information($"Rendering tile ({getTile.TileX}, {getTile.TileY}, {getTile.Zoom})");

                using (var bitmap = new SKBitmap(128, 128))
                using (var canvas = new SKCanvas(bitmap))
                {
                    await _renderer.RenderAsync(_tileDB, canvas, getTile.TileX, getTile.TileY, getTile.Zoom, Log.Logger);

                    // TODO: higher/lower quality?
                    using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 80))
                    using (var stream = new MemoryStream())
                    {
                        data.SaveTo(stream);

                        Log.Information("Sending rendered tile");
                        return Convert.ToBase64String(stream.ToArray());
                    }
                }
            }
            else
            {
                return null;
            }
        }, () =>
        {
            Log.Error("Event bus subscriber error");
            Dispose();
            Log.CloseAndFlush();
            Environment.Exit(1);
        }));

        Log.Information("Started");

        while (true)
        {
            Thread.Sleep(1000);
        }
    }

    public void Dispose()
        => _eventBus.Close();
}
