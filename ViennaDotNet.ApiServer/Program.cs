using Serilog.Events;
using Serilog;
using System.ComponentModel;
using System;
using Uma.Uuid;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ApiServer.Utils;
using CliUtils;
using CliUtils.Exceptions;

namespace ViennaDotNet.ApiServer
{
    public static class Program
    {
        internal static EarthDB DB;
        internal static Catalog Catalog;

        internal static EventBusClient eventBus;
        internal static TappablesManager tappablesManager;

        public static void Main(string[] args)
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
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Information)
                .MinimumLevel.Override("ViennaDotNet.ApiServer.Authentication", LogEventLevel.Information)
                .CreateLogger();

            Log.Logger = log;

            Options options = new Options();
            options.addOption(Option.builder()
                .Option("port")
                .LongOpt("port")
                .HasArg()
                .ArgName("port")
                .Type(typeof(int))
                .Desc("Port to listen on, defaults to 80")
                .Build());
            options.addOption(Option.builder()
                .Option("db")
                .LongOpt("db")
                .HasArg()
                .ArgName("db")
                .Desc("Database path, defaults to ./earth.db")
                .Build());
            options.addOption(Option.builder()
                .Option("eventbus")
                .LongOpt("eventbus")
                .HasArg()
                .ArgName("eventbus")
                .Desc("Event bus address, defaults to localhost:5532")
                .Build());
            options.addOption(Option.builder()
                .Option("objectstore")
                .LongOpt("objectstore")
                .HasArg()
                .ArgName("objectstore")
                .Desc("Object storage address, defaults to localhost:5396")
                .Build());
            options.addOption(Option.builder()
                .Option("previewGenerator")
                .LongOpt("previewGenerator")
                .HasArg()
                .ArgName("command")
                //.Required()
                .Desc("Command to run the buildplate preview generator")
                .Build());

            CommandLine commandLine;
            int httpPort;
            string dbConnectionString;
            string eventBusConnectionString;
            string objectStoreConnectionString;
            string buildplatePreviewGeneratorCommand;
#if !DEBUG
            try
            {
#endif
                commandLine = new DefaultParser().parse(options, args);
                httpPort = commandLine.hasOption("port") ? commandLine.getParsedOptionValue<int>("port") : 80;
                dbConnectionString = commandLine.hasOption("db") ? commandLine.getOptionValue("db")! : "./earth.db";
                eventBusConnectionString = commandLine.hasOption("eventbus") ? commandLine.getOptionValue("eventbus")! : "localhost:5532";
                objectStoreConnectionString = commandLine.hasOption("objectstore") ? commandLine.getOptionValue("objectstore")! : "localhost:5396";
                buildplatePreviewGeneratorCommand = commandLine.getOptionValue("previewGenerator")!;
#if !DEBUG
            }
            catch (ParseException exception)
            {
                Log.Fatal(exception.ToString());
                Environment.Exit(1);
                return;
            }
#endif

            Catalog = new Catalog();

            Log.Information("Connecting to database");
            try
            {
                DB = EarthDB.Open(dbConnectionString);
            }
            catch (EarthDB.DatabaseException ex)
            {
                Log.Fatal("Could not connect to database", ex);
                Environment.Exit(1);
                return;
            }
            Log.Information("Connected to database");

            Log.Information("Connecting to event bus");
            try
            {
                eventBus = EventBusClient.create(eventBusConnectionString);
            }
            catch (EventBusClientException ex)
            {
                Log.Fatal("Could not connect to event bus", ex);
                Environment.Exit(1);
                return;
            }
            Log.Information("Connected to event bus");

            tappablesManager = new TappablesManager(eventBus);

            CreateHostBuilder(args, httpPort).Build().Run();

            Log.Information("Server started!");
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
