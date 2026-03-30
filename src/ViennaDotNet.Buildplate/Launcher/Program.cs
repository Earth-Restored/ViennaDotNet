using CommandLine;
using Serilog;
using System.Diagnostics;
using ViennaDotNet.Common;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.Buildplate.Launcher;

internal static class Program
{
    internal static string StaticDataPath = "./staticdata";

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private sealed class Options
    {
        [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
        public string EventBusConnectionString { get; set; }
        [Option("publicAddress", Required = true, HelpText = "Public server address to report in instance info")]
        public string PublicAddress { get; set; }
        [Option("bridgeJar", Required = true, HelpText = "Fountain bridge JAR file")]
        public string BridgeJar { get; set; }
        [Option("serverTemplateDir", Required = true, HelpText = "Minecraft/Fabric server template directory, containing the Fabric JAR, mods, and libraries")]
        public string ServerTemplateDir { get; set; }
        [Option("fabricJarName", Required = true, HelpText = "Name of the Fabric JAR to run within the server template directory")]
        public string FabricJarName { get; set; }
        [Option("connectorPluginJar", Required = true, HelpText = "Fountain connector plugin JAR")]
        public string ConnectorPluginJar { get; set; }

        [Option("dir", Default = "./staticdata", Required = false, HelpText = "Static data path")]
        public string StaticDataPath { get; set; }

        [Option("logger-url", Default = null, Required = false, HelpText = "Url to send logs to")]
        public string? LoggerUrl { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    private static int Main(string[] args)
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

        StaticDataPath = options.StaticDataPath;

        var loggerConfig = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/buildplate_launcher/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.WithProperty("ComponentName", "BuildplateLauncher");

        if (!string.IsNullOrWhiteSpace(options.LoggerUrl))
        {
            loggerConfig.WriteTo.Http(options.LoggerUrl, 10 * 1024 * 1024);
        }

        loggerConfig.MinimumLevel.Debug();
        var log = loggerConfig.CreateLogger();

        Log.Logger = log;

        Log.Information("Connecting to event bus");
        EventBusClient eventBusClient;
        try
        {
            eventBusClient = EventBusClient.Create(options.EventBusConnectionString);
        }
        catch (EventBusClientException ex)
        {
            Log.Fatal($"Could not connect to event bus: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connected to event bus");

        string javaCmd = JavaLocator.Locate();
        Starter starter = new Starter(eventBusClient, options.EventBusConnectionString, options.PublicAddress, javaCmd, options.BridgeJar, options.ServerTemplateDir, options.FabricJarName, options.ConnectorPluginJar);
        InstanceManager instanceManager = new InstanceManager(eventBusClient, starter);

        Console.CancelKeyPress += (sender, e) =>
        {
            Log.Information("Ctrl+C received");
            instanceManager.Shutdown();
            e.Cancel = true;
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            instanceManager.Shutdown();
        };

        while (true)
        {
            Thread.Sleep(100);
        }
    }
}
