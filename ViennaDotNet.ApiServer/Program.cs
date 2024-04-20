using Serilog.Events;
using Serilog;
using System.ComponentModel;
using System;
using Uma.Uuid;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;

namespace ViennaDotNet.ApiServer
{
    public static class Program
    {
        internal static EarthDB DB;
        internal static Catalog Catalog;

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

            DB = EarthDB.Open("mydb.db");

            Catalog = new Catalog();

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
