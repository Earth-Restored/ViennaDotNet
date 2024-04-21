using Serilog.Events;
using Serilog;
using System.ComponentModel;
using System;
using Uma.Uuid;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ApiServer.Utils;

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
                .MinimumLevel.Override("Microsoft", LogEventLevel.Debug)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Debug)
                .MinimumLevel.Override("ViennaDotNet.ApiServer.Authentication", LogEventLevel.Debug)
                .CreateLogger();

            Log.Logger = log;

            Catalog = new Catalog();

            Log.Information("Connecting to database");
            try
            {
                DB = EarthDB.Open("mydb.db");
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
                eventBus = EventBusClient.create("localhost:5532"/*eventBusConnectionString*/);
            }
            catch (EventBusClientException ex)
            {
                Log.Fatal("Could not connect to event bus", ex);
                Environment.Exit(1);
                return;
            }
            Log.Information("Connected to event bus");

            tappablesManager = new TappablesManager(eventBus);

            CreateHostBuilder(args).Build().Run();

            Log.Information("Server started!");
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
