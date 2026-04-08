using CommandLine;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using System.ComponentModel;
using System.Diagnostics;
using Uma.Uuid;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.BuildplateImporter;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;
using ViennaDotNet.StaticData;
using SData = ViennaDotNet.StaticData.StaticData;

namespace ViennaDotNet.ApiServer;

public static class Program
{
    // initialized in main
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    internal static Config config;

    internal static EarthDB DB;
    internal static SData staticData;
    internal static EventBusClient eventBus;
    internal static ObjectStoreClient objectStore;
    internal static TappablesManager tappablesManager;
    internal static BuildplateInstancesManager buildplateInstancesManager;
    internal static Importer importer;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    private sealed class Options
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        [Option("port", Default = 80, Required = false, HelpText = "Port to listen on")]
        public int HttpPort { get; set; }

        [Option("earth-db", Default = "./earth.db", Required = false, HelpText = "Earth database connection string")]
        public string EarthDatabaseConnectionString { get; set; }

        [Option("live-db", Default = "./live.db", Required = false, HelpText = "Live database connection string")]
        public string LiveDatabaseConnectionString { get; set; }

        [Option("dir", Default = "./staticdata", Required = false, HelpText = "Static data path")]
        public string StaticDataPath { get; set; }

        [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
        public string EventBusConnectionString { get; set; }

        [Option("objectstore", Default = "localhost:5396", Required = false, HelpText = "Object storage address")]
        public string ObjectStoreConnectionString { get; set; }

        [Option("logger-url", Default = null, Required = false, HelpText = "Url to send logs to")]
        public string? LoggerUrl { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    public static async Task<int> Main(string[] args)
    {
        // when ran from a tool, don't try to parse args as Options
        if (args.Any(a => a.StartsWith("--applicationName", StringComparison.Ordinal)))
        {
            CreateHostBuilder(args, 8080, "").Build();
            return 0;
        }

        TypeDescriptor.AddAttributes(typeof(Uuid), new TypeConverterAttribute(typeof(StringToUuidConv)));

        Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

        /*var log = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/api_server/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("ViennaDotNet.ApiServer.Authentication", LogEventLevel.Warning)
            .CreateLogger();*/

        if (!Debugger.IsAttached)
        {
            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                Log.Fatal($"Unhandeled exception: {e.ExceptionObject}");
                Log.CloseAndFlush();
                Environment.Exit(1);
            };
        }

        ParserResult<Options> res = Parser.Default.ParseArguments<Options>(args);

        Options options;
        if (res is Parsed<Options> parsed)
            options = parsed.Value;
        else if (res is NotParsed<Options> notParsed)
        {
            if (res.Errors.Any(error => error is HelpRequestedError))
                return 0;
            else if (res.Errors.Any(error => error is VersionRequestedError))
                return 0;
            else
                return 1;
        }
        else
            return 1;

        var loggerConfig = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/api_server/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.WithProperty("ComponentName", "ApiServer");

        if (!string.IsNullOrWhiteSpace(options.LoggerUrl))
        {
            loggerConfig.WriteTo.Http(options.LoggerUrl, 10 * 1024 * 1024);
        }

        loggerConfig
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Information)
            .MinimumLevel.Override("ViennaDotNet.ApiServer.Authentication", LogEventLevel.Information);
        var log = loggerConfig.CreateLogger();

        Log.Logger = log;

        Log.Information("Loading configuration");
        try
        {
            const string configFileName = "api_config.json";
            if (!File.Exists(configFileName))
            {
                config = Config.Default;
                File.WriteAllText(configFileName, Json.SerializeIndented(config));
                Log.Information($"Configuration file not found or invalid, created with default values: {Path.GetFullPath(configFileName)}");
            }
            else
            {
                config = Json.Deserialize<Config>(File.ReadAllText(configFileName)) ?? Config.Default;
            }
        }
        catch (Exception ex)
        {
            Log.Fatal($"Failed to load configuration: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Loaded configuration");

        Log.Information("Connecting to earth database");
        try
        {
            DB = EarthDB.Open(options.EarthDatabaseConnectionString);
        }
        catch (EarthDB.DatabaseException ex)
        {
            Log.Fatal($"Could not connect to database: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connected to earth database");

        Log.Information("Connecting to event bus");
        try
        {
            eventBus = await EventBusClient.ConnectAsync(options.EventBusConnectionString);
        }
        catch (EventBusClientException ex)
        {
            Log.Fatal($"Could not connect to event bus: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connected to event bus");
        Log.Information("Connecting to object storage");
        try
        {
            objectStore = await ObjectStoreClient.ConnectAsync(options.ObjectStoreConnectionString);
        }
        catch (ObjectStoreClientException ex)
        {
            Log.Fatal($"Could not connect to object storage: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connected to object storage");

        Log.Information("Loading static data");
        try
        {
            staticData = new SData(options.StaticDataPath);
        }
        catch (StaticDataException staticDataException)
        {
            Log.Fatal($"Failed to load static data: {staticDataException}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Loaded static data");

        Log.Information("Importing shop buildplates");

        EarthDB.ObjectResults? currentShopBuildplates = null;
        try
        {
            currentShopBuildplates = await new EarthDB.ObjectQuery(true)
                .GetBuildplates(staticData.Buildplates.ShopBuildplates.Select(buildplate => buildplate.Id))
                .ExecuteAsync(DB);
        }
        catch (EarthDB.DatabaseException ex)
        {
            Log.Error($"Failed to get current shop buildplates: {ex}");
        }

        importer = new Importer(DB, eventBus, objectStore, Log.Logger);
        if (currentShopBuildplates is not null)
        {
            foreach (var buidplate in staticData.Buildplates.ShopBuildplates)
            {
                if (currentShopBuildplates.GetBuildplate(buidplate.Id) is not null)
                {
                    Log.Debug($"Shop buildplate {buidplate.Id} already exists");
                    continue;
                }

                try
                {
                    Log.Information($"Importing shop buildplate {buidplate.Id}");

                    string name = buidplate.Id;
                    if (Guid.TryParse(buidplate.Id, out var buidplateGuid))
                    {
                        var bpPlayfabItem = staticData.Playfab.Items.Values.FirstOrDefault(item => item.Data is Playfab.Item.BuildplateData bpData && bpData.Id == buidplateGuid);
                        if (bpPlayfabItem is not null)
                        {
                            name = bpPlayfabItem.Title;
                        }
                    }

                    using (var buidplateData = buidplate.OpenRead())
                    {
                        await importer.ImportTemplateAsync(buidplate.Id, $"[SHOP] {name}", buidplateData);
                    }
                }
                catch (Exception ex)
                {
                    Log.Fatal($"Failed to import shop buidplate {buidplate.Id}: {ex}");
                    Log.CloseAndFlush();
                    return 1;
                }
            }
        }

        Log.Information("Imported shop buidplates");

        tappablesManager = new TappablesManager(eventBus);
        buildplateInstancesManager = new BuildplateInstancesManager(eventBus);

        BuildplateInstanceRequestHandler.Start(DB, eventBus, objectStore, staticData.Catalog);

        var host = CreateHostBuilder(args, options.HttpPort, options.LiveDatabaseConnectionString).Build();

        Log.Information("Updating live db");
        using (var scope = host.Services.CreateScope())
        {
            var liveDb = scope.ServiceProvider.GetRequiredService<LiveDbContext>();
            liveDb.Database.Migrate();
        }

        Log.Information("Updated live db");

        host.Run();

        return 0;
    }

    public static IHostBuilder CreateHostBuilder(string[] args, int httpPort, string liveDbConnectionString)
        => Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddInMemoryCollection([
                    new("ConnectionStrings:LiveDBConnection", "Data Source=" + liveDbConnectionString)
                ]);
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
                webBuilder.UseUrls($"http://*:{httpPort}/");
            });
}
