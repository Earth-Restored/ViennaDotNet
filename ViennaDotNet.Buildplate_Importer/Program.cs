using CliUtils;
using CliUtils.Exceptions;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Buffers.Text;
using System.IO.Compression;
using System.Text;
using Uma.Uuid;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.Buildplate_Importer
{
    internal static class Program
    {
        static async Task<int> Main(string[] args)
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
                .Option("objectstore")
                .LongOpt("objectstore")
                .HasArg()
                .ArgName("objectstore")
                .Desc("Object storage address, defaults to localhost:5396")
                .Build());
            options.addOption(Option.builder()
                .Option("eventbus")
                .LongOpt("eventbus")
                .HasArg()
                .ArgName("eventbus")
                .Desc("Event bus address, defaults to localhost:5532")
                .Build());
            options.addOption(Option.builder()
                .Option("playerId")
                .LongOpt("playerId")
                .HasArg()
                .Required()
                .ArgName("id")
                .Desc("Player ID to import for")
                .Build());
            options.addOption(Option.builder()
                .Option("worldDir")
                .LongOpt("worldDir")
                .HasArg()
                .Required()
                .ArgName("dir")
                .Desc("World to import")
                .Build());
            CommandLine commandLine;
            string dbConnectionString;
            string objectStoreConnectionString;
            string eventBusConnectionString;
            string playerId;
            string worldDir;
            try
            {
                commandLine = new DefaultParser().parse(options, args);
                dbConnectionString = commandLine.hasOption("db") ? commandLine.getOptionValue("db")! : "./earth.db";
                objectStoreConnectionString = commandLine.hasOption("objectstore") ? commandLine.getOptionValue("objectstore")! : "localhost:5396";
                eventBusConnectionString = commandLine.hasOption("eventbus") ? commandLine.getOptionValue("eventbus")! : "localhost:5532";
                playerId = commandLine.getOptionValue("playerId")!.ToLowerInvariant();
                worldDir = commandLine.getOptionValue("worldDir")!;
            }
            catch (ParseException exception)
            {
                Log.Fatal(exception.ToString());
                return 1;
            }

            Log.Information("Connecting to database");
            EarthDB earthDB;
            try
            {
                earthDB = EarthDB.Open(dbConnectionString);
            }
            catch (EarthDB.DatabaseException exception)
            {
                Log.Fatal($"Could not connect to database: {exception}");
                return 1;
            }
            Log.Information("Connected to database");

            Log.Information("Connecting to object storage");
            ObjectStoreClient objectStoreClient;
            try
            {
                objectStoreClient = ObjectStoreClient.create(objectStoreConnectionString);
            }
            catch (ObjectStoreClientException exception)
            {
                Log.Fatal($"Could not connect to object storage: {exception}");
                return 1;
            }
            Log.Information("Connected to object storage");

            Log.Information("Connecting to event bus");
            EventBusClient? eventBusClient;
            try
            {
                eventBusClient = EventBusClient.create(eventBusConnectionString);
                Log.Information("Connected to event bus");
            }
            catch (EventBusClientException exception)
            {
                Log.Warning($"Could not connect to event bus, buildplate preview will not be generated: {exception}");
                eventBusClient = null;
            }

            byte[]? serverData = createServerDataFromWorldDir(worldDir);
            if (serverData == null)
            {
                Log.Fatal("Could not get world data");
                return 2;
            }

            string buildplateId = U.RandomUuid().ToString();

            if (!await storeBuildplate(earthDB, eventBusClient, objectStoreClient, playerId, buildplateId, serverData, U.CurrentTimeMillis()))
            {
                Log.Fatal("Could not add buildplate");
                return 3;
            }

            Log.Information($"Added buildplate with ID {buildplateId} for player {playerId}");
            return 0;
        }

        private static byte[]? createServerDataFromWorldDir(string worldDir)
        {
            if (!Directory.Exists(worldDir))
            {
                Log.Error("World directory cannot be accessed");
                return null;
            }

            byte[] data;
            try
            {
                using MemoryStream byteArrayOutputStream = new MemoryStream();

                using (ZipArchive zipArchive = new ZipArchive(byteArrayOutputStream, ZipArchiveMode.Create))
                {
                    foreach (string dirName in new string[] { "region", "entities" })
                    {
                        string dir = Path.Combine(worldDir, dirName);
                        foreach (string regionName in new string[] { "r.0.0.mca", "r.0.-1.mca", "r.-1.0.mca", "r.-1.-1.mca" })
                        {
                            ZipArchiveEntry zipEntry = zipArchive.CreateEntry(dirName + "/" + regionName, CompressionLevel.Optimal);
                            using (FileStream fileInputStream = File.OpenRead(Path.Combine(dir, regionName)))
                            using (Stream zipEntryStream = zipEntry.Open())
                                fileInputStream.CopyTo(zipEntryStream);
                        }
                    }
                }

                data = byteArrayOutputStream.ToArray();
            }
            catch (IOException exception)
            {
                Log.Error($"Could not get saved world data from world directory: {exception}");
                return null;
            }
            return data;
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
            catch (EarthDB.DatabaseException exception)
            {
                Log.Error($"Failed to store buildplate in database: {exception}");
                objectStoreClient.delete(serverDataObjectId);
                objectStoreClient.delete(previewObjectId);
                return false;
            }
        }
    }
}
