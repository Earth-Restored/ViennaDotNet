using CliUtils;
using CliUtils.Exceptions;
using Serilog;
using ViennaDotNet.DB;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.Buildplate.Launcher
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .MinimumLevel.Debug()
                .CreateLogger();

            Log.Logger = log;

            Options options = new Options();
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
                .Option("api")
                .LongOpt("api")
                .HasArg()
                .ArgName("address")
                .Required()
                .Desc("API server address")
                .Build());
            options.addOption(Option.builder()
                .Option("apiToken")
                .LongOpt("apiToken")
                .HasArg()
                .ArgName("token")
                .Required()
                .Desc("API server token")
                .Build());
            options.addOption(Option.builder()
                .Option("publicAddress")
                .LongOpt("publicAddress")
                .HasArg()
                .ArgName("address")
                .Required()
                .Desc("Public server address to report in instance info")
                .Build());
            options.addOption(Option.builder()
                .Option("bridgeJar")
                .LongOpt("bridgeJar")
                .HasArg()
                .ArgName("jar")
                .Required()
                .Desc("Fountain bridge JAR file")
                .Build());
            options.addOption(Option.builder()
                .Option("serverTemplateDir")
                .LongOpt("serverTemplateDir")
                .HasArg()
                .ArgName("dir")
                .Required()
                .Desc("Minecraft/Fabric server template directory, containing the Fabric JAR, mods, and libraries")
                .Build());
            options.addOption(Option.builder()
                .Option("fabricJarName")
                .LongOpt("fabricJarName")
                .HasArg()
                .ArgName("name")
                .Required()
                .Desc("Name of the Fabric JAR to run within the server template directory")
                .Build());
            options.addOption(Option.builder()
                .Option("connectorPluginJar")
                .LongOpt("connectorPluginJar")
                .HasArg()
                .ArgName("jar")
                .Required()
                .Desc("Fountain connector plugin JAR")
                .Build());

            CommandLine commandLine;
            string dbConnectionString;
            string eventBusConnectionString;
            string objectStoreConnectionString;
            string apiServerAddress;
            string apiServerToken;
            string publicAddress;
            string bridgeJar;
            string serverTemplateDir;
            string fabricJarName;
            string connectorPluginJar;
            try
            {
                commandLine = new DefaultParser().parse(options, args);
                dbConnectionString = commandLine.hasOption("db") ? commandLine.getOptionValue("db")! : "./earth.db";
                eventBusConnectionString = commandLine.hasOption("eventbus") ? commandLine.getOptionValue("eventbus")! : "localhost:5532";
                objectStoreConnectionString = commandLine.hasOption("objectstore") ? commandLine.getOptionValue("objectstore")! : "localhost:5396";
                apiServerAddress = commandLine.getOptionValue("api")!;
                apiServerToken = commandLine.getOptionValue("apiToken")!;
                publicAddress = commandLine.getOptionValue("publicAddress")!;
                bridgeJar = commandLine.getOptionValue("bridgeJar")!;
                serverTemplateDir = commandLine.getOptionValue("serverTemplateDir")!;
                fabricJarName = commandLine.getOptionValue("fabricJarName")!;
                connectorPluginJar = commandLine.getOptionValue("connectorPluginJar")!;
            }
            catch (ParseException exception)
            {
                Log.Fatal(exception.ToString());
                Environment.Exit(1);
                return;
            }

            Log.Information("Connecting to database");
            EarthDB earthDB;
            try
            {
                earthDB = EarthDB.Open(dbConnectionString);
            }
            catch (EarthDB.DatabaseException exception)
            {
                Log.Fatal("Could not connect to database", exception);
                Environment.Exit(1);
                return;
            }
            Log.Information("Connected to database");

            Log.Information("Connecting to event bus");
            EventBusClient eventBusClient;
            try
            {
                eventBusClient = EventBusClient.create(eventBusConnectionString);
            }
            catch (EventBusClientException exception)
            {
                Log.Fatal($"Could not connect to event bus: {exception}");
                Environment.Exit(1);
                return;
            }
            Log.Information("Connected to event bus");

            Log.Information("Connecting to object storage");
            ObjectStoreClient objectStoreClient;
            try
            {
                objectStoreClient = ObjectStoreClient.create(objectStoreConnectionString);
            }
            catch (ObjectStoreClientException exception)
            {
                Log.Fatal($"Could not connect to object storage: {exception}");
                Environment.Exit(1);
                return;
            }
            Log.Information("Connected to object storage");

            Starter starter = new Starter(earthDB, objectStoreClient, eventBusClient, eventBusConnectionString, apiServerAddress, apiServerToken, publicAddress, bridgeJar, serverTemplateDir, fabricJarName, connectorPluginJar);
            InstanceManager instanceManager = new InstanceManager(eventBusClient, starter);

            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                new Thread(() =>
                {
                    instanceManager.shutdown();
                });
            };
        }
    }
}
