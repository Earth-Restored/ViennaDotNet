using CommandLine;
using Newtonsoft.Json;
using Serilog;
using System.IO.Compression;
using System.Text;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.Buildplate_Importer
{
    internal static class Program
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        class Options
        {
            [Option("db", Default = "./earth.db", Required = false, HelpText = "Database connection string")]
            public string DatabaseConnectionString { get; set; }

            [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
            public string EventBusConnectionString { get; set; }

            [Option("objectstore", Default = "localhost:5396", Required = false, HelpText = "Object storage address")]
            public string ObjectStoreConnectionString { get; set; }

            [Option("id", Required = true, HelpText = "Player ID to import for")]
            public string PlayerId { get; set; }

            [Option("file", Required = true, HelpText = "World to import (directory or zip)")]
            public string WorldPath { get; set; }
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        static async Task Main(string[] args)
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
                    Environment.Exit(0);
                else if (res.Errors.Any(error => error is VersionRequestedError))
                    Environment.Exit(0);
                else
                    Environment.Exit(1);
                return;
            }
            else
            {
                Environment.Exit(1);
                return;
            }

            Log.Information("Connecting to database");
            EarthDB earthDB;
            try
            {
                earthDB = EarthDB.Open(options.DatabaseConnectionString);
            }
            catch (EarthDB.DatabaseException ex)
            {
                Log.Fatal($"Could not connect to database: {ex}");
                Environment.Exit(1);
                return;
            }
            Log.Information("Connected to database");

            Log.Information("Connecting to object storage");
            ObjectStoreClient objectStoreClient;
            try
            {
                objectStoreClient = ObjectStoreClient.create(options.ObjectStoreConnectionString);
            }
            catch (ObjectStoreClientException ex)
            {
                Log.Fatal($"Could not connect to object storage: {ex}");
                Environment.Exit(1);
                return;
            }
            Log.Information("Connected to object storage");

            Log.Information("Connecting to event bus");
            EventBusClient? eventBusClient;
            try
            {
                eventBusClient = EventBusClient.create(options.EventBusConnectionString);
                Log.Information("Connected to event bus");
            }
            catch (EventBusClientException ex)
            {
                Log.Warning($"Could not connect to event bus, buildplate preview will not be generated: {ex}");
                eventBusClient = null;
            }

            byte[]? serverData = createServerDataFromWorldPath(options.WorldPath);
            if (serverData == null)
            {
                Log.Fatal("Could not get world data");
                Environment.Exit(2);
                return;
            }

            string buildplateId = U.RandomUuid().ToString();

            if (!await storeBuildplate(earthDB, eventBusClient, objectStoreClient, options.PlayerId, buildplateId, serverData, U.CurrentTimeMillis()))
            {
                Log.Fatal("Could not add buildplate");
                Environment.Exit(3);
                return;
            }

            Log.Information($"Added buildplate with ID {buildplateId} for player {options.PlayerId}");
            Environment.Exit(0);
            return;
        }

        private static byte[]? createServerDataFromWorldPath(string worldPath)
        {
            if (File.Exists(worldPath))
            {
                try
                {
                    return File.ReadAllBytes(worldPath);
                }
                catch (IOException ex)
                {
                    Log.Error($"Could not read world file: {ex}");
                    return null;
                }
            }
            else if (Directory.Exists(worldPath))
            {
                try
                {
                    using MemoryStream byteArrayOutputStream = new MemoryStream();

                    using (ZipArchive zipArchive = new ZipArchive(byteArrayOutputStream, ZipArchiveMode.Create))
                    {
                        foreach (string dirName in new string[] { "region", "entities" })
                        {
                            string dir = Path.Combine(worldPath, dirName);
                            foreach (string regionName in new string[] { "r.0.0.mca", "r.0.-1.mca", "r.-1.0.mca", "r.-1.-1.mca" })
                            {
                                ZipArchiveEntry zipEntry = zipArchive.CreateEntry(dirName + "/" + regionName, CompressionLevel.Optimal);
                                using (FileStream fileInputStream = File.OpenRead(Path.Combine(dir, regionName)))
                                using (Stream zipEntryStream = zipEntry.Open())
                                    fileInputStream.CopyTo(zipEntryStream);
                            }
                        }
                    }

                    return byteArrayOutputStream.ToArray();
                }
                catch (IOException ex)
                {
                    Log.Error($"Could not get saved world data from world directory: {ex}");
                    return null;
                }
            } else
            {
                Log.Error("World file/directory cannot be accessed");
                return null;
            }
        }

        record PreviewRequest(
            string serverDataBase64,
            bool night
        )
        {
        }
        private static async Task<bool> storeBuildplate(EarthDB earthDB, EventBusClient? eventBusClient, ObjectStoreClient objectStoreClient, string playerId, string buildplateId, byte[] serverData, long timestamp)
        {
            string? preview;
            if (eventBusClient != null)
            {
                RequestSender requestSender = eventBusClient.addRequestSender();
                preview = await requestSender.request("buildplates", "preview", JsonConvert.SerializeObject(new PreviewRequest(Convert.ToBase64String(serverData), false))).Task;
                requestSender.close();

                if (preview == null)
                    Log.Warning("Could not get preview for buildplate (preview generator did not respond to event bus request)");
            }
            else
                preview = null;

            string? serverDataObjectId = (string?)await objectStoreClient.store(serverData).Task;
            if (serverDataObjectId == null)
            {
                Log.Error("Could not store data object in object store");
                return false;
            }

            string? previewObjectId = (string?)await objectStoreClient.store(preview != null ? Encoding.ASCII.GetBytes(preview) : Array.Empty<byte>()).Task;
            if (previewObjectId == null)
            {
                Log.Error("Could not store preview object in object store");
                return false;
            }

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("buildplates", playerId, typeof(Buildplates))
                    .Then(results1 =>
                    {
                        Buildplates buildplates = (Buildplates)results1.Get("buildplates").Value;

                        Buildplates.Buildplate buildplate = new Buildplates.Buildplate(16, 63, 33, false, timestamp, serverDataObjectId, previewObjectId);    // TODO: make size/offset/etc. configurable

                        buildplates.addBuildplate(buildplateId, buildplate);

                        return new EarthDB.Query(true)
                            .Update("buildplates", playerId, buildplates);
                    })
                    .Execute(earthDB);
                return true;
            }
            catch (EarthDB.DatabaseException ex)
            {
                Log.Error($"Failed to store buildplate in database: {ex}");
                objectStoreClient.delete(serverDataObjectId);
                objectStoreClient.delete(previewObjectId);
                return false;
            }
        }
    }
}
