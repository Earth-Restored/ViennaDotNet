using Npgsql;
using Serilog;
using SkiaSharp;
using System.Collections.Frozen;
using System.Text.Json;
using ViennaDotNet.TileRenderer.Wkb;

namespace ViennaDotNet.TileRenderer;

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
        { (int)RenderLayer.LAYER_HIGHWAY_MAJOR, (double)AreaType.HIGHWAY_SERVICE / 0xFF },
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
        Dictionary<string, Dictionary<string, RenderLayer>> tagsMap = [];

        logger.Information("Loading tags");

        using (JsonDocument doc = JsonDocument.Parse(tagMapJson))
        {
            foreach (JsonProperty tagField in doc.RootElement.EnumerateObject())
            {
                string tagName = tagField.Name;

                tags.Add(tagName);
                tagsMap[tagName] = [];

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
                        Log.Warning($"Unknown layer mapping '{tagMapping}'");
                        tagsMap[tagName][tagValue] = RenderLayer.LAYER_NONE;
                    }
                }
            }
        }

        return new TileRenderer(tags, tagsMap);
    }

    public async Task RenderAsync(NpgsqlDataSource dataSource, SKCanvas canvas, double latitude, double longtitute, int zoom, CancellationToken cancellationToken = default)
    {
        var (tileX, tileY) = getTileForCords(latitude, longtitute);

        Tile tile = new Tile(new Point(tileX, tileY), zoom, 128);

        canvas.Clear(LayerToColor((int)RenderLayer.LAYER_NONE));

        const string sql = @"
            SELECT aeroway, amenity, barrier, building, highway, landuse, leisure, military, ""natural"", railway, waterway, ST_AsBinary(way)
            FROM planet_osm_polygon
            WHERE way && ST_TileEnvelope(@zoom, @tileX, @tileY) AND boundary IS NULL
            UNION
            SELECT aeroway, amenity, barrier, building, highway, landuse, leisure, military, ""natural"", railway, waterway, ST_AsBinary(way)
            FROM planet_osm_line
            WHERE way && ST_TileEnvelope(@zoom, @tileX, @tileY)
              AND boundary IS NULL
              AND route IS NULL
              AND NOT (railway IS NULL AND highway IS NULL)
              AND (railway IS NULL OR railway != 'subway');";

        Log.Information("Loading map data");
        await using (var cmd = dataSource.CreateCommand(sql))
        {
            cmd.Parameters.AddWithValue("zoom", zoom);
            cmd.Parameters.AddWithValue("tileY", tileY);
            cmd.Parameters.AddWithValue("tileX", tileX);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            List<List<IWKBObject>> layers = [];
            for (int i = 0; i <= (int)RenderLayer.LAYER_NONE; i++)
            {
                layers.Add([]);
            }

            int rowCount = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowCount++;

                if (reader.IsDBNull(11)) // ST_AsBinary index
                {
                    continue;
                }

                RenderLayer targetLayer = RenderLayer.LAYER_NONE;

                foreach (string tagName in _tags)
                {
                    int ord = reader.GetOrdinal(tagName);
                    if (reader.IsDBNull(ord))
                    {
                        continue;
                    }

                    string tagValue = reader.GetString(ord);

                    if (_tagsMap.TryGetValue(tagName, out var valMap))
                    {
                        if (valMap.TryGetValue(tagValue, out var layer))
                        {
                            targetLayer = layer;
                            break;
                        }
                        else if (valMap.TryGetValue("*", out var defaultLayer))
                        {
                            targetLayer = defaultLayer;
                            break;
                        }
                    }
                }

                byte[] wkb = (byte[])reader[11];
                if (wkb.Length < 5)
                {
                    continue; // invalid
                }

                // Read WKB geometry type (skip endian byte at wkb[0])
                WkbGeometryType wkbType = (WkbGeometryType)BitConverter.ToUInt32(wkb, 1);

                using var ms = new MemoryStream(wkb);
                using var bReader = new BinaryReader(ms);

                IWKBObject? obj = wkbType switch
                {
                    WkbGeometryType.Point => null,
                    WkbGeometryType.MultiPoint => null,
                    WkbGeometryType.LineString => WKBLineString.Load(bReader),
                    WkbGeometryType.Polygon => WKBPolygon.Load(bReader),
                    WkbGeometryType.MultiLineString => WKBMultiLineString.Load(bReader),
                    WkbGeometryType.MultiPolygon => WKBMultiPolygon.Load(bReader),
                    _ => throw new Exception($"Unknown WKB type: {wkbType}"),
                };

                if (obj != null)
                {
                    layers[(int)targetLayer].Add(obj);
                }
            }

            Log.Information("Rendering image");
            for (int renderLayer = 0; renderLayer < (int)RenderLayer.LAYER_NONE; renderLayer++)
            {
                var layer = layers[renderLayer];

                for (int j = 0; j < layer.Count; j++)
                {
                    layer[j].Render(canvas, tile, LayerToColor(renderLayer), 2);
                }
            }

            canvas.Flush();
        }
    }

    private static SKColor LayerToColor(int layer)
    {
        byte bwColor = (byte)(layerColourMapping[layer] * 255);
        return new SKColor(bwColor, bwColor, bwColor);
    }

    static (int X, int Y) getTileForCords(double lat, double lon)
    {
        //Adapted from java example. Zoom is replaced by the constant 16 because all MCE tiles are at zoom 16

        int xtile = (int)Math.Floor((lon + 180) / 360 * (1 << 16));
        int ytile = (int)Math.Floor((1 - Math.Log(Math.Tan(toRadians(lat)) + 1 / Math.Cos(toRadians(lat))) / Math.PI) / 2 * (1 << 16));

        if (xtile < 0)
            xtile = 0;
        if (xtile >= (1 << 16))
            xtile = ((1 << 16) - 1);
        if (ytile < 0)
            ytile = 0;
        if (ytile >= (1 << 16))
            ytile = ((1 << 16) - 1);

        return (xtile, ytile);
    }

    //Helper
    static double toRadians(double angle)
    {
        return (Math.PI / 180) * angle;
    }
}
