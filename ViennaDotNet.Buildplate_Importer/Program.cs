using CommandLine;
using Serilog;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.Buildplate_Importer;

internal static class Program
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private sealed class Options
    {
        [Option("db", Default = "./earth.db", Required = false, HelpText = "Database connection string")]
        public string DatabaseConnectionString { get; set; }

        [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
        public string EventBusConnectionString { get; set; }

        [Option("objectstore", Default = "localhost:5396", Required = false, HelpText = "Object storage address")]
        public string ObjectStoreConnectionString { get; set; }

        [Option("id", Required = true, HelpText = "Player ID to import for")]
        public string PlayerId { get; set; }

        [Option("file", Required = true, HelpText = "World to import (.zip)")]
        public string WorldPath { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    private static async Task Main(string[] args)
    {
        var log = new LoggerConfiguration()
           .WriteTo.Console()
           .WriteTo.File("logs/buildplate_importer/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
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
            Log.CloseAndFlush();
            Environment.Exit(1);
            return;
        }

        Log.Information("Connected to database");

        Log.Information("Connecting to object storage");
        ObjectStoreClient objectStoreClient;
        try
        {
            objectStoreClient = ObjectStoreClient.Create(options.ObjectStoreConnectionString);
        }
        catch (ObjectStoreClientException ex)
        {
            Log.Fatal($"Could not connect to object storage: {ex}");
            Log.CloseAndFlush();
            Environment.Exit(1);
            return;
        }

        Log.Information("Connected to object storage");

        Log.Information("Connecting to event bus");
        EventBusClient? eventBusClient;
        try
        {
            eventBusClient = EventBusClient.Create(options.EventBusConnectionString);
            Log.Information("Connected to event bus");
        }
        catch (EventBusClientException ex)
        {
            Log.Warning($"Could not connect to event bus, buildplate preview will not be generated: {ex}");
            eventBusClient = null;
        }

        WorldData? worldData = ReadWorldFile(options.WorldPath);
        if (worldData is null)
        {
            Log.Fatal("Could not get world data");
            Log.CloseAndFlush();
            Environment.Exit(2);
            return;
        }

        string buildplateId = U.RandomUuid().ToString();

        string playerId = options.PlayerId.ToLowerInvariant();

        if (!await StoreBuildplate(earthDB, eventBusClient, objectStoreClient, playerId, buildplateId, worldData, U.CurrentTimeMillis()))
        {
            Log.Fatal("Could not add buildplate");
            Log.CloseAndFlush();
            Environment.Exit(3);
            return;
        }

        Log.Information($"Added buildplate with ID {buildplateId} for player {playerId}");
        Environment.Exit(0);
        return;
    }

    private sealed record BuildplateMetadataVersion(
        [property: JsonPropertyName("version")] int Version
    );

    private sealed record BuildplateMetadataV1(
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("offset")] int Offset,
        [property: JsonPropertyName("night")] bool Night
    );

    private static WorldData? ReadWorldFile(string worldFileName)
    {
        Dictionary<string, byte[]> worldFileContents = [];

        Span<Range> parts = stackalloc Range[3];

        try
        {
            using (var zip = ZipFile.OpenRead(worldFileName))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.IsDirectory())
                    {
                        continue;
                    }

                    var entryPath = entry.FullName.AsSpan().Trim(['/', '\\']);

                    if (entryPath is not "buildplate_metadata.json")
                    {
                        int partCount = entryPath.SplitAny(parts, ['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

                        if (partCount != 2)
                        {
                            continue;
                        }

                        if (entryPath[parts[0]] is not ("region" or "entities"))
                        {
                            continue;
                        }

                        if (entryPath[parts[1]] is not ("r.0.0.mca" or "r.0.-1.mca" or "r.-1.0.mca" or "r.-1.-1.mca"))
                        {
                            continue;
                        }
                    }

                    using (var stream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);

                        worldFileContents[entry.FullName] = ms.ToArray();
                    }
                }
            }
        }
        catch (IOException ex)
        {
            Log.Error($"Could not read world file: {ex}");
            return null;
        }

        byte[] serverData;
        try
        {
            using (var zipStream = new MemoryStream())
            {
                using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    foreach (string dirName in (IEnumerable<string>)["region", "entities"])
                    {
                        foreach (string fileName in (IEnumerable<string>)["r.0.0.mca", "r.0.-1.mca", "r.-1.0.mca", "r.-1.-1.mca"])
                        {
                            string filePath = $"{dirName}/{fileName}";

                            if (!worldFileContents.TryGetValue(filePath, out byte[]? data))
                            {
                                Log.Error($"World file is missing {filePath}");
                                return null;
                            }

                            var entry = zip.CreateEntry(filePath, CompressionLevel.SmallestSize);
                            using (var entryStream = entry.Open())
                            {
                                entryStream.Write(data);
                            }
                        }
                    }
                }

                serverData = zipStream.ToArray();
            }
        }
        catch (IOException ex)
        {
            Log.Error($"Could not prepare server data: {ex}");
            return null;
        }

        int size;
        int offset;
        bool night;

        try
        {
            byte[]? buildplateMetadataFileData = worldFileContents.GetValueOrDefault("buildplate_metadata.json");
            string? buildplateMetadataString = buildplateMetadataFileData is not null
                ? Encoding.UTF8.GetString(buildplateMetadataFileData)
                : null;

            if (buildplateMetadataString is null)
            {
                Log.Warning("World file does not contain buildplate_metadata.json, using default values");
                size = 16;
                offset = 63;
                night = false;
            }
            else
            {
                var buildplateMetadataVersion = Json.Deserialize<BuildplateMetadataVersion>(buildplateMetadataString);

                if (buildplateMetadataVersion is null)
                {
                    Log.Error("Invalid buildplate metadata");
                    return null;
                }

                switch (buildplateMetadataVersion.Version)
                {
                    case 1:
                        {
                            var buildplateMetadata = Json.Deserialize<BuildplateMetadataV1>(buildplateMetadataString);

                            if (buildplateMetadata is null)
                            {
                                Log.Error("Invalid buildplate metadata");
                                return null;
                            }

                            size = buildplateMetadata.Size;
                            offset = buildplateMetadata.Offset;
                            night = buildplateMetadata.Night;
                        }

                        break;
                    default:
                        {
                            Log.Error($"Unsupported buildplate metadata version {buildplateMetadataVersion.Version}");
                            return null;
                        }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Could not read buildplate metadata file: {ex}");
            return null;
        }

        if (size != 8 && size != 16 && size != 32)
        {
            Log.Error($"Invalid buildplate size {size}, must be on of: 8, 16, 32");
            return null;
        }

        return new WorldData(serverData, size, offset, night);
    }

    private sealed record PreviewRequest(
        string serverDataBase64,
        bool night
    );

    private static async Task<bool> StoreBuildplate(EarthDB earthDB, EventBusClient? eventBusClient, ObjectStoreClient objectStoreClient, string playerId, string buildplateId, WorldData worldData, long timestamp)
    {
        string? preview;
        if (eventBusClient is not null)
        {
            Log.Information("Generating preview");
            RequestSender requestSender = eventBusClient.AddRequestSender();
            preview = await requestSender.Request("buildplates", "preview", JsonSerializer.Serialize(new PreviewRequest(Convert.ToBase64String(worldData.ServerData), worldData.Night))).Task;
            requestSender.Close();

            if (preview is null)
            {
                Log.Warning("Could not get preview for buildplate (preview generator did not respond to event bus request)");
            }
        }
        else
        {
            Log.Information("Preview was not generated because event bus is not connected");
            preview = null;
        }

        Log.Information("Storing world");
        string? serverDataObjectId = (string?)await objectStoreClient.Store(worldData.ServerData).Task;
        if (serverDataObjectId is null)
        {
            Log.Error("Could not store data object in object store");
            return false;
        }

        Log.Information("Storing preview");
        string? previewObjectId = (string?)await objectStoreClient.Store(preview is not null ? Encoding.ASCII.GetBytes(preview) : []).Task;
        if (previewObjectId is null)
        {
            Log.Error("Could not store preview object in object store");
            return false;
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("buildplates", playerId, typeof(Buildplates))
                .Then(results1 =>
                {
                    Buildplates buildplates = (Buildplates)results1.Get("buildplates").Value;

                    int scale = worldData.Size switch
                    {
                        8 => 14,
                        16 => 33,
                        32 => 64,
                        _ => 33,
                    };

                    Buildplates.Buildplate buildplate = new Buildplates.Buildplate(worldData.Size, worldData.Offset, scale, worldData.Night, timestamp, serverDataObjectId, previewObjectId);

                    buildplates.AddBuildplate(buildplateId, buildplate);

                    return new EarthDB.Query(true)
                        .Update("buildplates", playerId, buildplates);
                })
                .ExecuteAsync(earthDB);
            return true;
        }
        catch (EarthDB.DatabaseException ex)
        {
            Log.Error($"Failed to store buildplate in database: {ex}");
            objectStoreClient.Delete(serverDataObjectId);
            objectStoreClient.Delete(previewObjectId);
            return false;
        }
    }

    private sealed record WorldData(
        byte[] ServerData,
        int Size,
        int Offset,
        bool Night
    );
}
