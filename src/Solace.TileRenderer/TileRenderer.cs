using Serilog;
using SkiaSharp;
using System.Collections.Frozen;
using System.Text.Json;
using Solace.TileRenderer.Wkb;

namespace Solace.TileRenderer;

public class TileRenderer
{
    // Map layers with their JSON string versions
    private static readonly FrozenDictionary<string, RenderLayer> layerStringMapping = new Dictionary<string, RenderLayer>()
    {
        { "RESTRICTED_AREA", RenderLayer.LAYER_RESTRICTED_AREA },
        { "HIGHWAY_MAJOR", RenderLayer.LAYER_HIGHWAY_MAJOR },
        { "HIGHWAY_MINOR", RenderLayer.LAYER_HIGHWAY_MINOR },
        { "HIGHWAY_SERVICE", RenderLayer.LAYER_HIGHWAY_SERVICE },
        { "CYCLE_PATH", RenderLayer.LAYER_CYCLE_PATH },
        { "MOUNTAIN", RenderLayer.LAYER_MOUNTAIN },
        { "SAND", RenderLayer.LAYER_SAND },
        { "PIER", RenderLayer.LAYER_PIER },
        { "FOOTPATH", RenderLayer.LAYER_FOOTPATH },
        { "WATER", RenderLayer.LAYER_WATER },
        { "ATHLETIC_FIELD", RenderLayer.LAYER_ATHLETIC_FIELD },
        { "OPEN_PRIVATE_AREA", RenderLayer.LAYER_OPEN_PRIVATE_AREA },
        { "OPEN_PUBLIC_AREA", RenderLayer.LAYER_OPEN_PUBLIC_AREA },
        { "FOREST", RenderLayer.LAYER_FOREST },
        { "BUILDING", RenderLayer.LAYER_BUILDING },
        { "BASE_BACKGROUND", RenderLayer.LAYER_BASE_BACKGROUND },
        { "_NO_RENDER", RenderLayer.LAYER_NONE }
    }.ToFrozenDictionary();

    // Map layers to their colours (normalised from 0-1)
    private static readonly FrozenDictionary<int, double> layerColourMapping = new Dictionary<int, double>()
    {
        { (int)RenderLayer.LAYER_BASE_BACKGROUND, (double)AreaType.BASE_BACKGROUND / 0xFF },
        { (int)RenderLayer.LAYER_OPEN_PUBLIC_AREA, (double)AreaType.OPEN_PUBLIC_AREA / 0xFF },
        { (int)RenderLayer.LAYER_OPEN_PRIVATE_AREA, (double)AreaType.OPEN_PRIVATE_AREA / 0xFF },
        { (int)RenderLayer.LAYER_ATHLETIC_FIELD, (double)AreaType.ATHLETIC_FIELD / 0xFF },
        { (int)RenderLayer.LAYER_SAND, (double)AreaType.SAND / 0xFF },
        { (int)RenderLayer.LAYER_FOREST, (double)AreaType.FOREST / 0xFF },
        { (int)RenderLayer.LAYER_WATER, (double)AreaType.WATER / 0xFF },
        { (int)RenderLayer.LAYER_PIER, (double)AreaType.PIER / 0xFF },
        { (int)RenderLayer.LAYER_MOUNTAIN, (double)AreaType.MOUNTAIN / 0xFF },
        { (int)RenderLayer.LAYER_BUILDING, (double)AreaType.BUILDING / 0xFF },
        { (int)RenderLayer.LAYER_FOOTPATH, (double)AreaType.FOOTPATH / 0xFF },
        { (int)RenderLayer.LAYER_CYCLE_PATH, (double)AreaType.CYCLE_PATH / 0xFF },
        { (int)RenderLayer.LAYER_HIGHWAY_SERVICE, (double)AreaType.HIGHWAY_SERVICE / 0xFF },
        { (int)RenderLayer.LAYER_HIGHWAY_MINOR, (double)AreaType.HIGHWAY_MINOR / 0xFF },
        { (int)RenderLayer.LAYER_HIGHWAY_MAJOR, (double)AreaType.HIGHWAY_MAJOR / 0xFF },
        { (int)RenderLayer.LAYER_RESTRICTED_AREA, (double)AreaType.RESTRICTED_AREA / 0xFF },
        { (int)RenderLayer.LAYER_NONE, (double)AreaType.BASE_BACKGROUND / 0xFF }
    }.ToFrozenDictionary();

    private readonly List<string> _tags;
    private readonly Dictionary<string, Dictionary<string, RenderLayer>> _tagsMap;

    private TileRenderer(List<string> tags, Dictionary<string, Dictionary<string, RenderLayer>> tagsMap)
    {
        _tags = tags;
        _tagsMap = tagsMap;
    }

    public static TileRenderer Create(string tagMapJson, ILogger logger)
    {
        List<string> tags = [];
        Dictionary<string, Dictionary<string, RenderLayer>> tagsMap = new(StringComparer.OrdinalIgnoreCase);

        logger.Information("Loading tags");

        using (var doc = JsonDocument.Parse(tagMapJson))
        {
            foreach (JsonProperty tagField in doc.RootElement.EnumerateObject())
            {
                string tagName = tagField.Name;

                tags.Add(tagName);
                tagsMap[tagName] = new(StringComparer.OrdinalIgnoreCase);

                foreach (JsonProperty valueField in tagField.Value.EnumerateObject())
                {
                    string tagValue = valueField.Name;
                    string tagMapping = "_NO_RENDER";

                    if (valueField.Value.ValueKind == JsonValueKind.String)
                    {
                        tagMapping = valueField.Value.GetString() ?? "";
                    }

                    if (layerStringMapping.TryGetValue(tagMapping, out RenderLayer layer))
                    {
                        tagsMap[tagName][tagValue] = layer;
                    }
                    else
                    {
                        logger.Warning($"Unknown layer mapping '{tagMapping}'");
                        tagsMap[tagName][tagValue] = RenderLayer.LAYER_NONE;
                    }
                }
            }
        }

        logger.Information("Loaded tags");

        return new TileRenderer(tags, tagsMap);
    }

    public async Task RenderAsync(ITileDataSource dataSource, SKCanvas canvas, int tileX, int tileY, int zoom, ILogger logger, CancellationToken cancellationToken = default)
    {
        var tile = new Tile(new Point(tileX, tileY), zoom, 128);

        canvas.Clear(LayerToColor((int)RenderLayer.LAYER_NONE));

        logger.Information("Loading map data");

        var layers = await dataSource.GetTileAsync(new RenderContext(_tags, _tagsMap), zoom, tileX, tileY, cancellationToken);

        logger.Information("Rendering image");
        for (int renderLayer = 0; renderLayer < (int)RenderLayer.LAYER_NONE; renderLayer++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var layer = layers[renderLayer];

            foreach (var obj in layer)
            {
                obj.Render(canvas, tile, LayerToColor(renderLayer), 2);
            }
        }

        canvas.Flush();
    }

    private static SKColor LayerToColor(int layer)
    {
        byte bwColor = (byte)(layerColourMapping[layer] * 255);
        return new SKColor(bwColor, bwColor, bwColor);
    }
}
