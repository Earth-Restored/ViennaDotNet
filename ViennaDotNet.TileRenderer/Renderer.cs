using Npgsql;
using Serilog;
using System.Collections.Frozen;
using System.Text.Json;

namespace ViennaDotNet.TileRenderer;

public class Renderer
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

    private Renderer(List<string> tags,  Dictionary<string, Dictionary<string, RenderLayer>> tagsMap)
    {
        _tags = tags;
        _tagsMap = tagsMap;
    }

    public static Renderer Create(string tagMapJson, ILogger logger)
    {
        List<string> tags = [];
        Dictionary<string, Dictionary<string, RenderLayer>> tagsMap = [];

        logger.Information("Loading tags");

        using (JsonDocument doc = JsonDocument.Parse(tagMapJson))
        {
            foreach (JsonProperty tagField in doc.RootElement.EnumerateObject())
            {
                string tagName = tagField.Name;
                Console.WriteLine($"- {tagName}");

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

                    Console.WriteLine($"  - {tagValue} : {tagMapping}");

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

        return new Renderer(tags, tagsMap);
    }

    public async Task RenderAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT aeroway, amenity, barrier, building, highway, landuse, leisure, military, ""natural"", railway, waterway, ST_AsBinary(way)
            FROM planet_osm_polygon
            WHERE way && ST_TileEnvelope(@tileZ, @tileX, @tileY) AND boundary IS NULL
            UNION
            SELECT aeroway, amenity, barrier, building, highway, landuse, leisure, military, ""natural"", railway, waterway, ST_AsBinary(way)
            FROM planet_osm_line
            WHERE way && ST_TileEnvelope(@tileZ, @tileX, @tileY)
              AND boundary IS NULL
              AND route IS NULL
              AND NOT (railway IS NULL AND highway IS NULL)
              AND (railway IS NULL OR railway != 'subway');";

        await using (var cmd = dataSource.CreateCommand(sql))
        {
            var (x, y) = getTileForCords(50.081604, 14.410044); // prague

            cmd.Parameters.AddWithValue("tileZ", 16);
            cmd.Parameters.AddWithValue("tileY", y);
            cmd.Parameters.AddWithValue("tileX", x);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);


        }
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
