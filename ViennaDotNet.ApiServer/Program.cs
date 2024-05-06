using CommandLine;
using Serilog;
using Serilog.Events;
using System.ComponentModel;
using Uma.Uuid;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.ApiServer
{
    public static class Program
    {
        // initialized in main
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        internal static EarthDB DB;
        internal static Catalog Catalog;

        internal static EventBusClient eventBus;
        internal static ObjectStoreClient objectStore;
        internal static TappablesManager tappablesManager;
        internal static BuildplateInstancesManager buildplateInstancesManager;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        class Options
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            [Option("port", Default = 80, Required = false, HelpText = "Port to listen on")]
            public int HttpPort { get; set; }

            [Option("db", Default = "./earth.db", Required = false, HelpText = "Database connection string")]
            public string DatabaseConnectionString { get; set; }

            [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
            public string EventBusConnectionString { get; set; }

            [Option("objectstore", Default = "localhost:5396", Required = false, HelpText = "Object storage address")]
            public string ObjectStoreConnectionString { get; set; }
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public static int Main(string[] args)
        {
            TypeDescriptor.AddAttributes(typeof(Uuid), new TypeConverterAttribute(typeof(StringToUuidConv)));

            //var log = new LoggerConfiguration()
            //    .WriteTo.Console()
            //    .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            //    .MinimumLevel.Debug()
            //    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            //    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            //    .MinimumLevel.Override("ProjectEarthServerAPI.Authentication", LogEventLevel.Warning)
            //    .CreateLogger();
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("ViennaDotNet.ApiServer.Authentication", LogEventLevel.Warning)
                .CreateLogger();

            Log.Logger = log;

            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                Log.Fatal($"Unhandeled exception: {e.ExceptionObject}");
                Log.CloseAndFlush();
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

            Log.Information("Connecting to database");
            try
            {
                DB = EarthDB.Open(options.DatabaseConnectionString);
            }
            catch (EarthDB.DatabaseException ex)
            {
                Log.Fatal($"Could not connect to database: {ex}");
                return 1;
            }
            Log.Information("Connected to database");

            Log.Information("Connecting to event bus");
            try
            {
                eventBus = EventBusClient.create(options.EventBusConnectionString);
            }
            catch (EventBusClientException ex)
            {
                Log.Fatal($"Could not connect to event bus: {ex}");
                return 1;
            }
            Log.Information("Connected to event bus");
            Log.Information("Connecting to object storage");
            try
            {
                objectStore = ObjectStoreClient.create(options.ObjectStoreConnectionString);
            }
            catch (ObjectStoreClientException ex)
            {
                Log.Fatal($"Could not connect to object storage: {ex}");
                return 1;
            }
            Log.Information("Connected to object storage");

            Catalog = new Catalog();

            tappablesManager = new TappablesManager(eventBus);
            buildplateInstancesManager = new BuildplateInstancesManager(eventBus);

            BuildplateInstanceRequestHandler.start(DB, eventBus, objectStore, Catalog);

            CreateHostBuilder(args, options.HttpPort).Build().Run();

            return 0;
        }

        public static IHostBuilder CreateHostBuilder(string[] args, int httpPort) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseUrls($"http://*:{httpPort}/");
                });
    }
}
