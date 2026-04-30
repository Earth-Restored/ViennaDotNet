namespace Solace.StaticData;

public sealed class TileRenderer
{
    public TileRenderer(string dir)
    {
        try
        {
            TagMap1Json = File.ReadAllText(Path.Combine(dir, "tagMap1.json"));
            TagMap2Json = File.ReadAllText(Path.Combine(dir, "tagMap2.json"));
        }
        catch (Exception exception)
        {
            throw new StaticDataException(null, exception);
        }
    }

    public string TagMap1Json { get; }

    public string TagMap2Json { get; }
}
