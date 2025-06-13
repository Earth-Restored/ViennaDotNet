using Newtonsoft.Json;
using Npgsql;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.TileRenderer;

internal sealed class EventBusTileRenderer
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly EventBusClient _eventBus;
    private readonly TileRenderer _renderer;
    private readonly Publisher _publisher;

    private readonly ConcurrentQueue<RenderTileRequest> _renderRequests = [];

    public EventBusTileRenderer(NpgsqlDataSource dataSource, EventBusClient eventBus, StaticData.StaticData staticData)
    {
        _dataSource = dataSource;
        _eventBus = eventBus;
        _renderer = TileRenderer.Create(staticData.tileRenderer.TagMapJson, Log.Logger);
        _publisher = _eventBus.addPublisher();
    }

    public void Run()
    {
        _eventBus.addRequestHandler("tile", new RequestHandler.Handler(request =>
        {
            if (request.type == "renderTile")
            {
                RenderTileRequest getTile;
                try
                {
                    getTile = JsonConvert.DeserializeObject<RenderTileRequest>(request.data)!;
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not deserialise active tile notification event: {ex}");
                    return null;
                }

                _renderRequests.Enqueue(getTile);

                return string.Empty;
            }
            else
            {
                return null;
            }
        }, () =>
        {
            Log.Error("Event bus subscriber error");
            Log.CloseAndFlush();
            Environment.Exit(1);
        }));

        while (true)
        {
            // use tasks to render tiles, save to object store, store id to db; respond with object store id? or no response?

            /*TileRenderer renderer = TileRenderer.Create(File.ReadAllText("tagMap.json"), log);

            using (var bitmap = new SKBitmap(128, 128))
            using (var canvas = new SKCanvas(bitmap))
            {
                await renderer.RenderAsync(dataSource, canvas, 50.081604, 14.410044, 16);

                using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 80))
                using (var stream = File.OpenWrite("tile.png"))
                {
                    Log.Information("Writing png...");
                    data.SaveTo(stream);
                }
            }*/
        }
    }
}
