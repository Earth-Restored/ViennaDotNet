using Serilog;
using System.Net;
using System.Text.Json;

namespace ViennaDotNet.Launcher;

public class Settings
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
    {
        WriteIndented = true,
    };

    public static readonly Settings Default = new Settings()
    {
        ApiPort = 80,
        EventBusPort = 5532,
        ObjectStorePort = 5396,
        IPv4 = "192.168.x.x",
        EarthDatabaseConnectionString = $".{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}earth.db",
        TileDatabaseConnectionString = "Host=localhost;Username=mylogin;Password=mypass;Database=genoa_tile_data",
        SkipFileChecks = false,
    };

    public ushort? ApiPort { get; set; }
    public ushort? EventBusPort { get; set; }
    public ushort? ObjectStorePort { get; set; }
    public string? IPv4 { get; set; }
    public string? EarthDatabaseConnectionString { get; set; }
    public string? TileDatabaseConnectionString { get; set; }

    public bool? SkipFileChecks { get; set; }

    public void Save(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(this, jsonOptions));

    public async Task SaveAsync(string path)
    {
        using (var fs = File.OpenWrite(path))
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

        if (settings.IPv4 is null || !IPAddress.TryParse(settings.IPv4, out var _))
        {
            Log.Warning($"IPv4 is invalid, using default: '{Default.IPv4}' (Change this in Options/IPv4)");
            settings.IPv4 = Default.IPv4;
            anyErrors = true;
        }

        if (settings.EarthDatabaseConnectionString is null)
        {
            Log.Warning($"DatabaseConnectionString is invalid, using default: '{Default.EarthDatabaseConnectionString}'");
            settings.EarthDatabaseConnectionString = Default.EarthDatabaseConnectionString;
            anyErrors = true;
        }

        if (settings.TileDatabaseConnectionString is null)
        {
            Log.Warning($"TileDatabaseConnectionString is invalid, using default: '{Default.TileDatabaseConnectionString}'");
            settings.TileDatabaseConnectionString = Default.TileDatabaseConnectionString;
            anyErrors = true;
        }

        if (settings.SkipFileChecks is null)
        {
            Log.Warning($"Skip file checks is invalid, using default: '{Default.SkipFileChecks}'");
            settings.SkipFileChecks = Default.SkipFileChecks;
            anyErrors = true;
        }

        Log.Information("Loaded settings");

        await settings.SaveAsync(path);

        if (anyErrors)
        {
            U.PAK();
        }

        return settings;
    }
}
