using CommandLine;
using Serilog;
using System.Diagnostics;

namespace ViennaDotNet.ObjectStore.Server;

internal static class Program
{
    private sealed class Options
    {
        [Option("dataDir", Default = "data", Required = false, HelpText = "Directory where data is stored")]
        public string DataDir { get; set; } = null!;

        [Option("port", Default = 5396, Required = false, HelpText = "Port to listen on")]
        public int Port { get; set; }

        [Option("logger-url", Default = null, Required = false, HelpText = "Url to send logs to")]
        public string? LoggerUrl { get; set; }
    }

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
                 .WriteTo.File("logs/object_store_server/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                 .Enrich.WithProperty("ComponentName", "ObjectStore");

        if (!string.IsNullOrWhiteSpace(options.LoggerUrl))
        {
            loggerConfig.WriteTo.Http(options.LoggerUrl, 10 * 1024 * 1024);
        }

        loggerConfig.MinimumLevel.Debug();
        var log = loggerConfig.CreateLogger();

        Log.Logger = log;

        NetworkServer server;
        try
        {
            server = new NetworkServer(new Server(new DataStore(new DirectoryInfo(options.DataDir))), options.Port);
        }
        catch (Exception ex) when (
            ex is IOException
            || ex is DataStore.DataStoreException
        )
        {
            Log.Fatal(ex.ToString());
            Log.CloseAndFlush();
            return 1;
        }

        await server.RunAsync();

        return 0;
    }
}
