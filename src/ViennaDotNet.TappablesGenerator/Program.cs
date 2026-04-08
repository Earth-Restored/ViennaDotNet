using CommandLine;
using Serilog;
using System.Diagnostics;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.TappablesGenerator;

internal static class Program
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private sealed class Options
    {
        [Option("dir", Default = "./staticdata", Required = false, HelpText = "Static data path")]
        public string StaticDataPath { get; set; }

        [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
        public string EventBusConnectionString { get; set; }

        [Option("logger-url", Default = null, Required = false, HelpText = "Url to send logs to")]
        public string? LoggerUrl { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    private static async Task<int> Main(string[] args)
    {
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
            .WriteTo.File("logs/tappable_generator/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.WithProperty("ComponentName", "TappableGenerator");

        if (!string.IsNullOrWhiteSpace(options.LoggerUrl))
        {
            loggerConfig.WriteTo.Http(options.LoggerUrl, 10 * 1024 * 1024);
        }

        loggerConfig.MinimumLevel.Debug();
        var log = loggerConfig.CreateLogger();

        Log.Logger = log;

        Log.Information("Loading static data");
        StaticData.StaticData staticData;
        try
        {
            staticData = new StaticData.StaticData(options.StaticDataPath);
        }
        catch (StaticDataException staticDataException)
        {
            Log.Fatal($"Failed to load static data: {staticDataException}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Loaded static data");

        Log.Information("Connecting to event bus");
        EventBusClient eventBusClient;
        try
        {
            eventBusClient = await EventBusClient.ConnectAsync(options.EventBusConnectionString);
        }
        catch (EventBusClientException ex)
        {
            Log.Fatal($"Could not connect to event bus: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connected to event bus");

        TappableGenerator tappableGenerator = new TappableGenerator(staticData);
        EncounterGenerator encounterGenerator = new EncounterGenerator(staticData);
        Spawner[] spawner = new Spawner[1];
        ActiveTiles activeTiles = new ActiveTiles(eventBusClient, new ActiveTiles.ActiveTileListener(
            async activeTiles =>
            {
                await spawner[0].SpawnTiles(activeTiles);
            },
            async activeTile =>
            {
                // empty
            }
        ));
        spawner[0] = new Spawner(eventBusClient, activeTiles, tappableGenerator, encounterGenerator);
        await spawner[0].Run();

        return 0;
    }
}
