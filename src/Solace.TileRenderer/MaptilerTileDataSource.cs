using NetTopologySuite.IO.VectorTiles.Mapbox;
using Serilog;
using Solace.TileRenderer.Wkb;

namespace Solace.TileRenderer;

internal sealed class MaptilerTileDataSource : ITileDataSource
{
    private readonly string _apiKey;
    private readonly int _maxZoom;
    private readonly HttpClient _httpClient;

    public MaptilerTileDataSource(string apiKey, int maxZoom, HttpClient? httpClient)
    {
        _apiKey = apiKey;
        _maxZoom = maxZoom;
        _httpClient = httpClient ?? new HttpClient();
    }

    public string GetTagMapJson(StaticData.TileRenderer tileRenderer)
        => tileRenderer.TagMap2Json;

    public async Task<List<List<IWKBObject>>> GetTileAsync(RenderContext ctx, int zoom, int tileX, int tileY, CancellationToken cancellationToken = default)
    {
        if (zoom > _maxZoom)
        {
            // TODO: cache surrounding data - return which tiles were fetched?
            tileX = tileX / (1 << (zoom - _maxZoom));
            tileY = tileY / (1 << (zoom - _maxZoom));

            zoom = _maxZoom;
        }

        var response = await _httpClient.GetAsync($"https://api.maptiler.com/tiles/v3/{zoom}/{tileX}/{tileY}.pbf?key={_apiKey}", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        try
        {
            var reader = new MapboxTileReader();

            var tileDefinition = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(tileX, tileY, zoom);

            var tile = reader.Read(await response.Content.ReadAsStreamAsync(cancellationToken), tileDefinition);

            List<List<IWKBObject>> layers = [];
            for (int i = 0; i <= (int)RenderLayer.LAYER_NONE; i++)
            {
                layers.Add([]);
            }

            foreach (var layer in tile.Layers)
            {
                var features = layer.Features;
                foreach (var feature in features)
                {
                    string? featureClass = feature.Attributes.GetOptionalValue("class") as string;
                    if (!ctx.TryGetLayer(layer.Name, featureClass ?? "*", out var targetLayer))
                    {
                        if (layer.Name is "landcover" or /*"housenumber" or*/ "poi" or "transportation_name")
                        {
                            targetLayer = RenderLayer.LAYER_NONE;
                        }
                        else if (layer.Name is "housenumber")
                        {
                            targetLayer = RenderLayer.LAYER_BUILDING;
                        }
                        else
                        {
                            targetLayer = RenderLayer.LAYER_NONE;
                        }
                    }

                    if (layer.Name is "waterway" && feature.Attributes.GetOptionalValue("brunnel") is "tunnel")
                    {
                        targetLayer = RenderLayer.LAYER_NONE;
                    }

                    var geometry = feature.Geometry;
                    IWKBObject? obj = geometry switch
                    {
                        NetTopologySuite.Geometries.Point => null,
                        NetTopologySuite.Geometries.MultiPoint => null,
                        NetTopologySuite.Geometries.LineString lineString => new WKBLineString(BitConverter.IsLittleEndian, 2, (uint)lineString.SRID, [.. lineString.Coordinates.Select(CoordinateToPoint)]),
                        NetTopologySuite.Geometries.Polygon polygon => ConvertPolygon(polygon),
                        NetTopologySuite.Geometries.MultiLineString multiLineString => new WKBMultiLineString(BitConverter.IsLittleEndian, 5, (uint)multiLineString.SRID, [.. multiLineString.Geometries.Select(lineString => new WKBLineString(BitConverter.IsLittleEndian, 2, (uint)lineString.SRID, [.. lineString.Coordinates.Select(CoordinateToPoint)]))]),
                        NetTopologySuite.Geometries.MultiPolygon multiPolygon => new WKBMultiPolygon(BitConverter.IsLittleEndian, 6, (uint)multiPolygon.SRID, [.. multiPolygon.Geometries.Select(ConvertPolygon)]),
                        _ => throw new InvalidDataException($"Unknown type: {geometry.GetType().FullName}"),
                    };

                    if (obj is not null)
                    {
                        layers[(int)targetLayer].Add(obj);
                    }
                }
            }

            return layers;
        }
        catch (Exception ex)
        {
            Log.Error(ex.ToString());

            List<List<IWKBObject>> layers = [];
            for (int i = 0; i <= (int)RenderLayer.LAYER_NONE; i++)
            {
                layers.Add([]);
            }

            return layers;
        }
    }

    private static WKBPolygon ConvertPolygon(NetTopologySuite.Geometries.Geometry gPolygon)
    {
        var polygon = (NetTopologySuite.Geometries.Polygon)gPolygon;
        //return new WKBPolygon(BitConverter.IsLittleEndian, 3, (uint)polygon.SRID, [.. polygon.InteriorRings.Select(ring => new LinearRing([.. ring.Coordinates.Select(CoordinateToPoint)])));
        return new WKBPolygon(BitConverter.IsLittleEndian, 3, (uint)polygon.SRID, [new LinearRing([.. polygon.ExteriorRing.Coordinates.Select(CoordinateToPoint)])]);
    }

    private static Point CoordinateToPoint(NetTopologySuite.Geometries.Coordinate coord)
    {
        double EarthRadius = 6378137;

        return new Point(double.DegreesToRadians(coord.X), double.Log(double.Tan(double.Pi / 4d + double.DegreesToRadians(coord.Y) / 2d))) * EarthRadius;
    }

    public void Dispose()
        => _httpClient.Dispose();
}
