using Npgsql;
using Solace.TileRenderer.Wkb;

namespace Solace.TileRenderer;

internal sealed class DatabaseTileDataSource : ITileDataSource
{
    private readonly NpgsqlDataSource _dataSource;

    public DatabaseTileDataSource(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public string GetTagMapJson(StaticData.TileRenderer tileRenderer)
        => tileRenderer.TagMap1Json;

    public async Task<List<List<IWKBObject>>> GetTileAsync(RenderContext ctx, int zoom, int tileX, int tileY, CancellationToken cancellationToken = default)
    {
        const string Sql = @"
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

        await using (var cmd = _dataSource.CreateCommand(Sql))
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

                foreach (string tagName in ctx.Tags)
                {
                    int ord = reader.GetOrdinal(tagName);
                    if (reader.IsDBNull(ord))
                    {
                        continue;
                    }

                    string tagValue = reader.GetString(ord);

                    if (ctx.TryGetLayer(tagName, tagValue, out targetLayer))
                    {
                        break;
                    }
                }

                byte[] wkb = (byte[])reader[11];
                if (wkb.Length < 5)
                {
                    continue; // invalid
                }

                // Read WKB geometry type (skip endian byte at wkb[0])
                var wkbType = (WkbGeometryType)BitConverter.ToUInt32(wkb, 1);

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
                    _ => throw new InvalidDataException($"Unknown WKB type: {wkbType}"),
                };

                if (obj is not null)
                {
                    layers[(int)targetLayer].Add(obj);
                }
            }

            return layers;
        }
    }

    public void Dispose()
        => _dataSource.Dispose();
}