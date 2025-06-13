namespace ViennaDotNet.StaticData;

public sealed class StaticData
{
    public readonly Catalog catalog;
    public readonly Levels levels;
    public readonly TappablesConfig tappablesConfig;
    public readonly EncountersConfig encountersConfig;
    public readonly TileRenderer tileRenderer;

    public StaticData(string dir)
    {
        catalog = new Catalog(Path.Combine(dir, "catalog"));
        levels = new Levels(Path.Combine(dir, "levels"));
        tappablesConfig = new TappablesConfig(Path.Combine(dir, "tappables"));
        tileRenderer = new TileRenderer(Path.Combine(dir, "tile_renderer"));
    }
}