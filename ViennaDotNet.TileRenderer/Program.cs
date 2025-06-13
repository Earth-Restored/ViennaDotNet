using CommandLine;
using Npgsql;
using Serilog;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.StaticData;
using ViennaDotNet.TileRenderer;

internal static class Program
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private sealed class Options
    {
        [Option("db", Required = true, HelpText = "!NOT earth.db! Connection string to a postgresql database with tile data, for example 'Host=myserver;Username=mylogin;Password=mypass;Database=mydatabase'")]
        public string DatabaseConnectionString { get; set; }

        [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
        public string EventBusConnectionString { get; set; }

        [Option("dir", Default = "./data", Required = false, HelpText = "Static data path")]
        public string StaticDataPath { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    private static async Task<int> Main(string[] args)
    {
        var log = new LoggerConfiguration()
           .WriteTo.Console()
           .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
           .MinimumLevel.Debug()
           .CreateLogger();

        Log.Logger = log;

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
        {
            options = parsed.Value;
        }
        else
        {
            return res is NotParsed<Options> notParsed
                ? res.Errors.Any(error => error is HelpRequestedError) 
                    ? 0 
                    : res.Errors.Any(error => error is VersionRequestedError) 
                    ? 0 
                    : 1
                : 1;
        }

        await using var dataSource = NpgsqlDataSource.Create(options.DatabaseConnectionString);

        NpgsqlDataSource db;
        Log.Information("Connecting to database");
        try
        {
            db = NpgsqlDataSource.Create(options.DatabaseConnectionString);
        }
        catch (Exception ex)
        {
            Log.Fatal($"Could not connect to database: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connecting to event bus");
        EventBusClient eventBusClient;
        try
        {
            eventBusClient = EventBusClient.create(options.EventBusConnectionString);
        }
        catch (EventBusClientException ex)
        {
            db.Dispose();

            Log.Fatal($"Could not connect to event bus: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Loading static data");
        StaticData staticData;
        try
        {
            staticData = new StaticData(options.StaticDataPath);
        }
        catch (StaticDataException staticDataException)
        {
            db.Dispose();

            Log.Fatal($"Failed to load static data: {staticDataException}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Loaded static data");

        Log.Information("Connected to event bus");

        EventBusTileRenderer renderer = new EventBusTileRenderer(db, eventBusClient, staticData);

        renderer.Run();

        return 0;
    }
}