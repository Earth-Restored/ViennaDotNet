namespace ViennaDotNet.StaticData;

public sealed class StaticData
{
    public readonly Catalog catalog;
    public readonly TappablesConfig tappablesConfig;
    public readonly EncountersConfig encountersConfig;

    public StaticData(string dir)
    {
        catalog = new Catalog(Path.Combine(dir, "catalog"));
        tappablesConfig = new TappablesConfig(Path.Combine(dir, "tappables"));
        encountersConfig = new EncountersConfig(Path.Combine(dir, "encounters"));
    }
}
