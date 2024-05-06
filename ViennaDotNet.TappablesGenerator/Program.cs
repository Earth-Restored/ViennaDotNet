using CommandLine;
using Serilog;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.TappablesGenerator
{
    internal static class Program
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        class Options
        {
            [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
            public string EventBusConnectionString { get; set; }
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        static int Main(string[] args)
        {
            var log = new LoggerConfiguration()
               .WriteTo.Console()
               .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
               .MinimumLevel.Debug()
               .CreateLogger();

            Log.Logger = log;

            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                Log.Fatal($"Unhandeled exception: {e.ExceptionObject}");
                Environment.Exit(1);
            };

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

            Log.Information("Connecting to event bus");
            EventBusClient eventBusClient;
            try
            {
                eventBusClient = EventBusClient.create(options.EventBusConnectionString);
            }
            catch (EventBusClientException ex)
            {
                Log.Fatal($"Could not connect to event bus: {ex}");
                return 1;
            }
            Log.Information("Connected to event bus");

            Generator generator = new Generator();
            Spawner[] spawner = new Spawner[1];
            ActiveTiles activeTiles = new ActiveTiles(eventBusClient, new ActiveTiles.ActiveTileListener(
                activeTile =>
                {
                    spawner[0].spawnTile(activeTile.tileX, activeTile.tileY);
                },
                activeTile =>
                {
                    // empty
                }
            ));
            spawner[0] = new Spawner(eventBusClient, activeTiles, generator);
            spawner[0].run();

            return 0;
        }
    }
}
