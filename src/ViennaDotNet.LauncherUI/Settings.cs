using Serilog;
using System.Net;
using System.Text.Json;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.LauncherUI;

public sealed class Settings
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly Settings Default = new Settings()
    {
        ApiPort = 8080,
        EventBusPort = 5532,
        ObjectStorePort = 5396,
        IPv4 = "192.168.x.x",
        EarthDatabaseConnectionString = Path.Combine(Program.DataDirRelative, "earth.db"),
        LiveDatabaseConnectionString = Path.Combine(Program.DataDirRelative, "live.db"),
        EnableTileRenderingLabel = true,
        TileDataSource = TileDataSourceEnum.MapTiler,
        MapTilerApiKey = null,
        TileDatabaseConnectionString = "Host=localhost;Username=mylogin;Password=mypass;Database=genoa_tile_data",
        GeneratePreviewOnImport = true,
        SkipFileChecks = false,
        StaticDataPath = "../staticdata",
        LauncherBuildplatePreview = false,
    };

    public static Settings Instance { get; set; } = Default;

    public static string DefaultPath => "config.json";

    public ushort? ApiPort { get; set; }
    public ushort? EventBusPort { get; set; }
    public ushort? ObjectStorePort { get; set; }
    public string? IPv4 { get; set; }

    public string? EarthDatabaseConnectionString { get; set; }
    public string? LiveDatabaseConnectionString { get; set; }

    public bool? EnableTileRenderingLabel { get; set; }
    public TileDataSourceEnum? TileDataSource { get; set; }
    public string? MapTilerApiKey { get; set; }
    public string? TileDatabaseConnectionString { get; set; }

    public bool? GeneratePreviewOnImport { get; set; } // TODO: is this really needed?
    public bool? SkipFileChecks { get; set; }

    public string? StaticDataPath {get;set;}

    public bool? LauncherBuildplatePreview { get; set; }

    public enum TileDataSourceEnum
    {
        MapTiler,
        PostgreSQL,
    }

    public void Save(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(this, jsonOptions));

    public async Task SaveAsync(string path)
    {
        using (var fs = File.OpenWriteNew(path))
        {
            await JsonSerializer.SerializeAsync(fs, this, jsonOptions);
        }
    }

    public static async Task<Settings> LoadAsync(string path)
    {
        Log.Information("Loading settings...");

        Settings? settings;

        if (!File.Exists(path))
        {
            Log.Information($"Config file doesn't exist, created default");
            settings = Default;
        }
        else
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    settings = await JsonSerializer.DeserializeAsync<Settings>(fs, jsonOptions);
                }

                if (settings is null)
                {
                    throw new Exception("Settings is null");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Error when parsing settings, using default: {ex}");
                settings = Default;
            }
        }

        bool anyErrors = false;
        if (settings.ApiPort is null)
        {
            Log.Warning($"Api port is invalid, using default: '{Default.ApiPort}'");
            settings.ApiPort = Default.ApiPort;
            anyErrors = true;
        }

        if (settings.EventBusPort is null)
        {
            Log.Warning($"EventBus port is invalid, using default: '{Default.EventBusPort}'");
            settings.EventBusPort = Default.EventBusPort;
            anyErrors = true;
        }

        if (settings.ObjectStorePort is null)
        {
            Log.Warning($"ObjectStore port is invalid, using default: '{Default.ObjectStorePort}'");
            settings.ObjectStorePort = Default.ObjectStorePort;
            anyErrors = true;
        }

        UriHostNameType nameType = Uri.CheckHostName(settings.IPv4);

        if (nameType != UriHostNameType.IPv4 && nameType != UriHostNameType.Dns)
        {
            Log.Warning($"IPv4 is invalid, using default: '{Default.IPv4}' (Change this in Options/IPv4)");
            settings.IPv4 = Default.IPv4;
            anyErrors = true;
        }

        if (string.IsNullOrWhiteSpace(settings.EarthDatabaseConnectionString))
        {
            Log.Warning($"DatabaseConnectionString is invalid, using default: '{Default.EarthDatabaseConnectionString}'");
            settings.EarthDatabaseConnectionString = Default.EarthDatabaseConnectionString;
            anyErrors = true;
        }

        if (string.IsNullOrWhiteSpace(settings.LiveDatabaseConnectionString))
        {
            Log.Warning($"LiveDatabaseConnectionString is invalid, using default: '{Default.LiveDatabaseConnectionString}'");
            settings.LiveDatabaseConnectionString = Default.LiveDatabaseConnectionString;
            anyErrors = true;
        }

        if (settings.EnableTileRenderingLabel is null)
        {
            Log.Warning($"EnableTileRenderingLabel is invalid, using default: '{Default.EnableTileRenderingLabel}'");
            settings.EnableTileRenderingLabel = Default.EnableTileRenderingLabel;
            anyErrors = true;
        }

        if (settings.EnableTileRenderingLabel is true)
        {
            if (settings.TileDataSource is null)
            {
                Log.Warning($"TileDataSource is invalid, using default: '{Default.TileDataSource}'");
                settings.TileDataSource = Default.TileDataSource;
                anyErrors = true;
            }

            if (string.IsNullOrWhiteSpace(settings.MapTilerApiKey))
            {
                Log.Warning($"MapTilerApiKey is invalid, using default: '{Default.MapTilerApiKey}'");
                settings.MapTilerApiKey = Default.MapTilerApiKey;
                anyErrors = true;
            }
        }

        if (settings.TileDatabaseConnectionString is null)
        {
            Log.Warning($"TileDatabaseConnectionString is invalid, using default: '{Default.TileDatabaseConnectionString}'");
            settings.TileDatabaseConnectionString = Default.TileDatabaseConnectionString;
            anyErrors = true;
        }

        if (settings.GeneratePreviewOnImport is null)
        {
            Log.Warning($"Generate preview on import is invalid, using default: '{Default.GeneratePreviewOnImport}'");
            settings.GeneratePreviewOnImport = Default.GeneratePreviewOnImport;
            anyErrors = true;
        }

        if (settings.SkipFileChecks is null)
        {
            Log.Warning($"Skip file checks is invalid, using default: '{Default.SkipFileChecks}'");
            settings.SkipFileChecks = Default.SkipFileChecks;
            anyErrors = true;
        }

        if (string.IsNullOrWhiteSpace(settings.StaticDataPath))
        {
            Log.Warning($"StaticData path is invalid, using default: '{Default.StaticDataPath}'");
            settings.StaticDataPath = Default.StaticDataPath;
            anyErrors = true;
        }

        Log.Information("Loaded settings");

        await settings.SaveAsync(path);

        return settings;
    }
}
