namespace ViennaDotNet.StaticData;

public readonly struct StaticBuidplate
{
    private readonly string _path;

    internal StaticBuidplate(string path)
    {
        _path = path;
    }

    public string Id => Path.GetFileNameWithoutExtension(_path);

    public Stream OpenRead()
        => File.OpenRead(_path);
}
