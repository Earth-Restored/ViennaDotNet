namespace ViennaDotNet.StaticData;

public sealed class Buildplates
{
    private const string ShopDirectory = "shop";

    private readonly string _directory;

    internal Buildplates(string dir)
    {
        _directory = dir;

        Directory.CreateDirectory(Path.Combine(_directory, ShopDirectory));
    }

    public IEnumerable<StaticBuidplate> ShopBuildplates => Directory.EnumerateFiles(Path.Combine(_directory, ShopDirectory))
        .Select(path => new StaticBuidplate(path));
}
