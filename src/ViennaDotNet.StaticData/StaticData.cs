namespace ViennaDotNet.StaticData;

public sealed class StaticData
{
    public readonly string Directory;

    private Catalog? _catalog;
    private PlayerLevels? _levels;
    private TappablesConfig? _tappablesConfig;
    private EncountersConfig? _encountersConfig;
    private TileRenderer? _tileRenderer;
    private Buildplates? _buildplates;
    private Playfab? _playfab;

    public StaticData(string dir)
    {
        Directory = Path.GetFullPath(dir);
    }

    public Catalog Catalog => _catalog ??= new Catalog(Path.Combine(Directory, "catalog"));

    public PlayerLevels Levels => _levels ??= new PlayerLevels(Path.Combine(Directory, "levels"));

    public TappablesConfig TappablesConfig => _tappablesConfig ??= new TappablesConfig(Path.Combine(Directory, "tappables"));

    public EncountersConfig EncountersConfig => _encountersConfig ??= new EncountersConfig(Path.Combine(Directory, "encounters"));

    public TileRenderer TileRenderer => _tileRenderer ??= new TileRenderer(Path.Combine(Directory, "tile_renderer"));

    public Buildplates Buildplates => _buildplates ??= new Buildplates(Path.Combine(Directory, "buildplates"));

    public Playfab Playfab => _playfab ??= new Playfab(Path.Combine(Directory, "playfab"));
}