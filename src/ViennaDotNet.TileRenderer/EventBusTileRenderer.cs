using Serilog;
using SkiaSharp;
using ViennaDotNet.Common;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.TileRenderer;

internal sealed class EventBusTileRenderer : IAsyncDisposable
{
    private readonly ITileDataSource _dataSource;
    private readonly EventBusClient _eventBus;
    private readonly TileRenderer _renderer;

    public EventBusTileRenderer(ITileDataSource dataSource, EventBusClient eventBus, StaticData.StaticData staticData)
    {
        _dataSource = dataSource;
        _eventBus = eventBus;
        _renderer = TileRenderer.Create(dataSource.GetTagMapJson(staticData.TileRenderer), Log.Logger);
    }

    public async Task RunAsync()
    {
        await _eventBus.AddRequestHandlerAsync("tile", new RequestHandlerLister(async request =>
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
                    await _renderer.RenderAsync(_dataSource, canvas, getTile.TileX, getTile.TileY, getTile.Zoom, Log.Logger);

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
        }, async () =>
        {
            Log.Error("Event bus subscriber error");
            await DisposeAsync();
            Log.CloseAndFlush();
            Environment.Exit(1);
        }));

        Log.Information("Started");

        while (true)
        {
            await Task.Delay(1000);
        }
    }

    public async ValueTask DisposeAsync()
        => await _eventBus.DisposeAsync();
}
