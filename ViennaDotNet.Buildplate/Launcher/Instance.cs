using Cyotek.Data.Nbt;
using Cyotek.Data.Nbt.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Serilog;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using ViennaDotNet.Buildplate.Connector.Model;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Buildplate.Connector.Model;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.Buildplate.Launcher;

public class Instance
{
    private const int HOST_PLAYER_CONNECT_TIMEOUT = 20000;

    public static Instance run(EventBusClient eventBusClient, string? playerId, string buildplateId, BuildplateSource buildplateSource, string instanceId, bool survival, bool night, bool saveEnabled, InventoryType inventoryType, long? shutdownTime, string publicAddress, int port, int serverInternalPort, string javaCmd, FileInfo fountainBridgeJar, DirectoryInfo serverTemplateDir, string fabricJarName, FileInfo connectorPluginJar, DirectoryInfo baseDir, string eventBusConnectionstring)
    {
        if (playerId is null && buildplateSource == BuildplateSource.PLAYER)
        {
            throw new ArgumentException($"{nameof(playerId)} was not while {nameof(buildplateSource)} was {nameof(BuildplateSource.PLAYER)}.", nameof(playerId));
        }

        Instance instance = new Instance(eventBusClient, playerId, buildplateId, buildplateSource, instanceId, survival, night, saveEnabled, inventoryType, shutdownTime, publicAddress, port, serverInternalPort, javaCmd, fountainBridgeJar, serverTemplateDir, fabricJarName, connectorPluginJar, baseDir, eventBusConnectionstring);

        new Thread(instance.run)
        {
            Name = $"Instance {instanceId}"
        }.Start();

        return instance;
    }

    private readonly EventBusClient eventBusClient;

    private readonly string? playerId;
    private readonly string buildplateId;
    private readonly BuildplateSource buildplateSource;
    public readonly string instanceId;
    private readonly bool survival;
    private readonly bool night;
    private readonly bool saveEnabled;
    private readonly InventoryType inventoryType;
    private readonly long? shutdownTime;

    public readonly string publicAddress;
    public readonly int port;
    private readonly int serverInternalPort;

    private readonly string javaCmd;
    private readonly FileInfo fountainBridgeJar;
    private readonly DirectoryInfo serverTemplateDir;
    private readonly string fabricJarName;
    private readonly FileInfo connectorPluginJar;
    private readonly DirectoryInfo baseDir;
    private readonly string eventBusAddress;
    private readonly string eventBusQueueName;
    private readonly string connectorPluginArgString;

    private Thread thread;

    private Publisher? publisher = null;
    private RequestSender? requestSender = null;

    private Subscriber? subscriber = null;
    private RequestHandler? requestHandler = null;

    private DirectoryInfo? serverWorkDir;
    private DirectoryInfo? bridgeWorkDir;
    private ConsoleProcess? serverProcess = null;
    private ConsoleProcess? bridgeProcess = null;
    private bool shuttingDown = false;
    private readonly object subprocessLock = new();

    private volatile bool hostPlayerConnected = false;

    private Instance(EventBusClient eventBusClient, string? playerId, string buildplateId, BuildplateSource buildplateSource, string instanceId, bool survival, bool night, bool saveEnabled, InventoryType inventoryType, long? shutdownTime, string publicAddress, int port, int serverInternalPort, string javaCmd, FileInfo fountainBridgeJar, DirectoryInfo serverTemplateDir, string fabricJarName, FileInfo connectorPluginJar, DirectoryInfo baseDir, string eventBusConnectionString)
    {
        this.eventBusClient = eventBusClient;

        this.playerId = playerId;
        this.buildplateId = buildplateId;
        this.buildplateSource = buildplateSource;
        this.instanceId = instanceId;
        this.survival = survival;
        this.night = night;
        this.saveEnabled = saveEnabled;
        this.inventoryType = inventoryType;
        this.shutdownTime = shutdownTime;

        this.publicAddress = publicAddress;
        this.port = port;
        this.serverInternalPort = serverInternalPort;

        this.javaCmd = javaCmd;
        this.fountainBridgeJar = fountainBridgeJar;
        this.serverTemplateDir = serverTemplateDir;
        this.fabricJarName = fabricJarName;
        this.connectorPluginJar = connectorPluginJar;
        this.baseDir = baseDir;
        this.eventBusAddress = eventBusConnectionString;
        this.eventBusQueueName = "buildplate_" + this.instanceId;
        this.connectorPluginArgString = JsonConvert.SerializeObject(new ConnectorPluginArg(this.eventBusAddress, this.eventBusQueueName, this.inventoryType)).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private void run()
    {
        thread = Thread.CurrentThread;

        try
        {
            Log.Information(buildplateSource switch
            {
                BuildplateSource.PLAYER => $"Starting for player {playerId} buildplate {buildplateId} (survival = {survival}, saveEnabled = {saveEnabled}, inventoryType = {inventoryType})",
                BuildplateSource.SHARED => $"Starting for shared buildplate {buildplateId} (player = {playerId}, survival = {survival}, saveEnabled = {saveEnabled}, inventoryType = {inventoryType})",
                BuildplateSource.ENCOUNTER => $"Starting for encounter buildplate {buildplateId} (player = {playerId}, survival = {survival}, saveEnabled = {saveEnabled}, inventoryType = {inventoryType})",
                _ => throw new UnreachableException(),
            });
            Log.Information($"Using port {port} internal port {serverInternalPort}");

            publisher = eventBusClient.addPublisher();
            requestSender = eventBusClient.addRequestSender();

            Log.Information("Setting up server");

            BuildplateLoadResponse? buildplateLoadResponse = buildplateSource switch
            {
                BuildplateSource.PLAYER => sendEventBusRequestRaw<BuildplateLoadResponse>("load", new BuildplateLoadRequest(playerId!, buildplateId), true).Result,
                BuildplateSource.SHARED => sendEventBusRequestRaw<BuildplateLoadResponse>("loadShared", new SharedBuildplateLoadRequest(buildplateId), true).Result,
                BuildplateSource.ENCOUNTER => sendEventBusRequestRaw<BuildplateLoadResponse>("loadEncounter", new EncounterBuildplateLoadRequest(buildplateId), true).Result,
                _ => throw new UnreachableException(),
            };

            byte[] serverData;
            try
            {
                serverData = Convert.FromBase64String(buildplateLoadResponse.serverDataBase64);
            }
            catch
            {
                Log.Error("Buildplate load response contained invalid base64 data");
                return;
            }

            try
            {
                serverWorkDir = setupServerFiles(serverData);
                if (serverWorkDir == null)
                {
                    Log.Error("Could not set up files for server");
                    return;
                }
            }
            catch (IOException ex)
            {
                Log.Error($"Could not set up files for server: {ex}");
                return;
            }

            try
            {
                bridgeWorkDir = setupBridgeFiles(serverData);
                if (bridgeWorkDir == null)
                {
                    Log.Error("Could not set up files for bridge");
                    return;
                }
            }
            catch (IOException ex)
            {
                Log.Error("Could not set up files for bridge", ex);
                return;
            }

            Log.Information("Running server");

            subscriber = eventBusClient.addSubscriber(eventBusQueueName, new Subscriber.SubscriberListener(
                @event => handleConnectorEvent(@event),
                () =>
                {
                    Log.Error("Event bus subscriber error");
                    beginShutdown();
                }
            ));
            requestHandler = eventBusClient.addRequestHandler(eventBusQueueName, new RequestHandler.Handler(
                request =>
                {
                    object? responseObject = handleConnectorRequest(request);
                    if (responseObject != null)
                        return JsonConvert.SerializeObject(responseObject);
                    else
                        return null;
                },
                () =>
                {
                    Log.Error("Event bus request handler error");
                    beginShutdown();
                }
            ));

            Monitor.Enter(subprocessLock);
            if (!shuttingDown)
            {
                startServerProcess();
                if (serverProcess != null)
                {
                    Monitor.Exit(subprocessLock);
                    int exitCode = waitForProcess(serverProcess.Process);
                    Monitor.Enter(subprocessLock);
                    serverProcess = null;
                    if (!shuttingDown)
                        Log.Warning($"Server process has unexpectedly terminated with exit code {exitCode}");
                    else
                        Log.Information($"Server has finished with exit code {exitCode}");

                    shuttingDown = true;

                    if (bridgeProcess != null)
                    {
                        Log.Information("Bridge is still running, shutting it down now");
                        bridgeProcess.StopAndWait();
                        Monitor.Exit(subprocessLock);
                        exitCode = waitForProcess(bridgeProcess.Process);
                        Monitor.Enter(subprocessLock);
                        bridgeProcess = null;
                        Log.Information($"Bridge has finished with exit code {exitCode}");
                    }
                }
                else
                    Log.Information("Server failed to start");
            }

            Monitor.Exit(subprocessLock);
        }
        catch (Exception ex)
        {
            Log.Error($"Unhandled exception: {ex}");
        }
        finally
        {
            subscriber?.close();

            requestHandler?.close();

            if (publisher is not null)
            {
                publisher.flush();
                publisher.close();
            }
            if (requestSender is not null)
            {
                requestSender.flush();
                requestSender.close();
            }

            cleanupBaseDir();

            Log.Information("Finished");
        }
    }

    private void handleConnectorEvent(Subscriber.Event @event)
    {
        switch (@event.type)
        {
            case "started":
                {
                    Log.Information("Server is ready");
                    startBridgeProcess();
                    sendEventBusInstanceStatusNotification("ready");
                    if (shutdownTime != null)
                    {
                        startShutdownTimer();
                    }
                    else
                    {
                        startHostPlayerConnectTimeout();
                    }
                }

                break;
            case "saved":
                {
                    if (saveEnabled)
                    {
                        WorldSavedMessage? worldSavedMessage = readJson<WorldSavedMessage>(@event.data);
                        if (worldSavedMessage is not null)
                        {
                            if (hostPlayerConnected)

                            {
                                Log.Information("Saving snapshot");
                                _ = sendEventBusRequest<object>("saved", worldSavedMessage, false);
                            }
                            else
                            {
                                Log.Information("Not saving snapshot because host player never connected");
                            }
                        }
                    }
                    else
                    {
                        Log.Information("Ignoring save data because saving is disabled");
                    }
                }

                break;

            case "inventoryAdd":
                {
                    InventoryAddItemMessage? inventoryAddItemMessage = readJson<InventoryAddItemMessage>(@event.data);
                    if (inventoryAddItemMessage != null)
                        sendEventBusRequest<object>("inventoryAdd", inventoryAddItemMessage, false);
                }

                break;
            case "inventoryUpdateWear":
                {
                    InventoryUpdateItemWearMessage? inventoryUpdateItemWearMessage = readJson<InventoryUpdateItemWearMessage>(@event.data);
                    if (inventoryUpdateItemWearMessage != null)
                        sendEventBusRequest<object>("inventoryUpdateWear", inventoryUpdateItemWearMessage, false);
                }

                break;
            case "inventorySetHotbar":
                {
                    InventorySetHotbarMessage? inventorySetHotbarMessage = readJson<InventorySetHotbarMessage>(@event.data);
                    if (inventorySetHotbarMessage != null)
                        sendEventBusRequest<object>("inventorySetHotbar", inventorySetHotbarMessage, false);
                }

                break;
        }
    }

    private object? handleConnectorRequest(RequestHandler.Request request)
    {
        switch (request.type)
        {
            case "playerConnected":
                {
                    PlayerConnectedRequest? playerConnectedRequest = readJson<PlayerConnectedRequest>(request.data);
                    if (playerConnectedRequest is not null)
                    {
                        if (playerId is not null && !hostPlayerConnected && playerConnectedRequest.uuid != playerId)
                        {
                            Log.Information($"Rejecting player connection for player {playerConnectedRequest.uuid} because the host player must connect first");
                            return new PlayerConnectedResponse(false, null);
                        }

                        PlayerConnectedResponse? playerConnectedResponse = sendEventBusRequest<PlayerConnectedResponse>("playerConnected", playerConnectedRequest, true).Result;
                        if (playerConnectedResponse is not null)
                        {
                            Log.Information($"Player {playerConnectedRequest.uuid} has connected");

                            if (playerId is not null && !hostPlayerConnected && playerConnectedRequest.uuid == playerId)
                            {
                                hostPlayerConnected = true;
                            }

                            return playerConnectedResponse;
                        }
                    }
                }

                break;
            case "playerDisconnected":
                {
                    Log.Debug("Player dicconnecting...");
                    PlayerDisconnectedRequest? playerDisconnectedRequest = readJson<PlayerDisconnectedRequest>(request.data);
                    if (playerDisconnectedRequest is not null)
                    {
                        PlayerDisconnectedResponse? playerDisconnectedResponse = sendEventBusRequest<PlayerDisconnectedResponse>("playerDisconnected", playerDisconnectedRequest, true).Result;
                        if (playerDisconnectedResponse is not null)
                        {
                            Log.Information($"Player {playerDisconnectedRequest.playerId} has disconnected");

                            if (shutdownTime is null && playerId is not null && playerDisconnectedRequest.playerId == playerId)
                            {
                                Log.Information("Host player has disconnected, beginning shutdown");
                                beginShutdown();
                            }

                            return playerDisconnectedResponse;
                        }
                    }
                }

                break;
            case "playerDead":
                {
                    string? playerId = readJson<string>(request.data);
                    if (playerId is not null)
                    {
                        bool? respawn = sendEventBusRequest<bool?>("playerDead", playerId, true).Result;
                        if (respawn is not null)
                        {
                            return respawn.Value;
                        }
                    }
                }

                break;
            case "getInventory":
                {
                    string? playerId = readJson<string>(request.data);
                    if (playerId is not null)
                    {
                        InventoryResponse? inventoryResponse = sendEventBusRequest<InventoryResponse>("getInventory", playerId, true).Result;

                        if (inventoryResponse is not null)
                            return inventoryResponse;
                    }
                }

                break;
            case "inventoryRemove":
                {
                    InventoryRemoveItemRequest? inventoryRemoveItemRequest = readJson<InventoryRemoveItemRequest>(request.data);
                    if (inventoryRemoveItemRequest is not null)
                    {
                        if (inventoryRemoveItemRequest.instanceId is not null)
                        {
                            bool? success = sendEventBusRequest<bool>("inventoryRemove", inventoryRemoveItemRequest, true).Result;

                            if (success is not null)
                                return success;
                        }
                        else
                        {
                            int? removedCount = sendEventBusRequest<int>("inventoryRemove", inventoryRemoveItemRequest, true).Result;
                            if (removedCount is not null)
                                return removedCount;
                        }
                    }
                }

                break;
            case "findPlayer":
                {
                    FindPlayerIdRequest? findPlayerIdRequest = readJson<FindPlayerIdRequest>(request.data);
                    if (findPlayerIdRequest is not null)
                    {
                        // TODO
                        return findPlayerIdRequest.minecraftName;
                    }
                }

                break;
            case "getInitialPlayerState":
                {
                    string? playerId = readJson<string>(request.data);
                    if (playerId != null)
                    {
                        InitialPlayerStateResponse? initialPlayerStateResponse = sendEventBusRequest<InitialPlayerStateResponse>("getInitialPlayerState", playerId, true).Result;
                        if (initialPlayerStateResponse != null)
                        {
                            return initialPlayerStateResponse;
                        }
                    }
                }

                break;
        }

        return null;
    }

    private T? readJson<T>(string str)
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(str);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to decode event bus message JSON: {ex}");
            beginShutdown();
            return default;
        }
    }

    private sealed record RequestWithInstanceId(
        string instanceId,
        object request
    );

    private void sendEventBusInstanceStatusNotification(string status)
    {
        Debug.Assert(publisher is not null);

        publisher.publish("buildplates", status, instanceId).ContinueWith(successTask =>
        {
            if (!successTask.Result)
            {
                Log.Error("Event bus publisher error");
                beginShutdown();
            }
        });
    }

    private Task<T?> sendEventBusRequest<T>(string type, object obj, bool returnResponse)
    {
        RequestWithInstanceId request = new RequestWithInstanceId(instanceId, obj);

        return sendEventBusRequestRaw<T>(type, request, returnResponse);
    }

    private async Task<T?> sendEventBusRequestRaw<T>(string type, object obj, bool returnResponse)
    {
        try
        {
            string? response = await requestSender!.request("buildplates", type, JsonConvert.SerializeObject(obj)).Task;

            if (response == null)
            {
                Log.Error("Event bus request failed (no response)");
                beginShutdown();
                return default;
            }

            if (returnResponse)
                return JsonConvert.DeserializeObject<T>(response);
            else
                return default;
        }
        catch (Exception ex)
        {
            Log.Error($"Event bus request failed: {ex}");
            beginShutdown();
            return default;
        }
    }

    private DirectoryInfo? setupServerFiles(byte[] serverData)
    {
        DirectoryInfo workDir = new DirectoryInfo(Path.Combine(baseDir.FullName, "server"));
        if (!workDir.TryCreate())
        {
            Log.Error("Could not create server working directory");
            return null;
        }

        if (!copyServerFile(Path.Combine(serverTemplateDir.FullName, fabricJarName), Path.Combine(workDir.FullName, fabricJarName), false))
        {

            Log.Error($"Fabric JAR {fabricJarName} does not exist in server template directory");
            return null;
        }

        bool warnedMissingServerFiles = false;
        if (!copyServerFile(Path.Combine(Path.Combine(serverTemplateDir.FullName, ".fabric"), "server"), Path.Combine(workDir.FullName, ".fabric/server"), true))
        {
            if (!warnedMissingServerFiles)
            {

                Log.Warning("Server files were not pre-downloaded in server template directory, it is recommended to pre-download all server files to improve instance start-up time and reduce network data usage");
                warnedMissingServerFiles = true;
            }
        }

        if (!copyServerFile(Path.Combine(serverTemplateDir.FullName, "libraries"), Path.Combine(workDir.FullName, "libraries"), true))
        {
            if (!warnedMissingServerFiles)
            {
                Log.Warning("Server files were not pre-downloaded in server template directory, it is recommended to pre-download all server files to improve instance start-up time and reduce network data usage");
                warnedMissingServerFiles = true;
            }
        }

        if (!copyServerFile(Path.Combine(serverTemplateDir.FullName, "versions"), Path.Combine(workDir.FullName, "versions"), true))
        {
            if (!warnedMissingServerFiles)
            {
                Log.Warning("Server files were not pre-downloaded in server template directory, it is recommended to pre-download all server files to improve instance start-up time and reduce network data usage");
                warnedMissingServerFiles = true;
            }
        }

        if (!copyServerFile(Path.Combine(serverTemplateDir.FullName, "mods"), Path.Combine(workDir.FullName, "mods"), true))
        {
            Log.Error("Mods directory was not present in server template directory, the buildplate server instance will not function correctly without the Fountain and Vienna Fabric mods installed");
        }

        File.WriteAllText(Path.Combine(workDir.FullName, "eula.txt"), "eula=true");

        string serverProperties = new StringBuilder()
            .Append("online-mode=false\n")
            .Append("enforce-secure-profile=false\n")
            .Append("sync-chunk-writes=false\n")
            .Append("spawn-protection=0\n")
            .Append($"server-port={serverInternalPort}\n")
            .Append($"gamemode={(survival ? "survival" : "creative")}\n")
            .Append($"vienna-event-bus-address={eventBusAddress}\n")
            .Append($"vienna-event-bus-queue-name={eventBusQueueName}\n")
            .ToString();
        File.WriteAllText(Path.Combine(workDir.FullName, "server.properties"), serverProperties);

        DirectoryInfo worldDir = new DirectoryInfo(Path.Combine(workDir.FullName, "world"));
        if (!worldDir.TryCreate())
        {
            Log.Error("Could not create server world directory");
            return null;
        }

        DirectoryInfo worldEntitiesDir = new DirectoryInfo(Path.Combine(worldDir.FullName, "entities"));
        if (!worldEntitiesDir.TryCreate())
        {
            Log.Error("Could not create server world entities directory");
            return null;
        }

        DirectoryInfo worldRegionDir = new DirectoryInfo(Path.Combine(worldDir.FullName, "region"));
        if (!worldRegionDir.TryCreate())
        {
            Log.Error("Could not create server world regions directory");
            return null;
        }

        TagCompound levelDatTag = createLevelDat(survival, night);
        using (FileStream fs = new FileStream(Path.Combine(worldDir.FullName, "level.dat"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
        using (GZipStream gzs = new GZipStream(fs, CompressionLevel.Optimal))
        {
            BinaryTagWriter writer = new BinaryTagWriter(gzs);
            writer.WriteStartDocument();
            writer.WriteStartTag(null, TagType.Compound);
            writer.WriteTag(levelDatTag);
            writer.WriteEndTag();
            writer.WriteEndDocument();
        }
        //NBTIO.writeFile(levelDatTag, new File(worldDir, "level.dat"));

        using (MemoryStream byteArrayInputStream = new MemoryStream(serverData))
        using (ZipArchive zipInputStream = new ZipArchive(byteArrayInputStream))
        {
            foreach (ZipArchiveEntry entry in zipInputStream.Entries)
            {
                if (entry.IsDirectory()) continue;

                string path = Path.Combine(worldDir.FullName, entry.FullName);

                using (Stream zipStream = entry.Open())
                using (FileStream fs = File.OpenWrite(path))
                    zipStream.CopyTo(fs);
            }
        }

        return workDir;
    }

    private static bool copyServerFile(string src, string dst, bool directory)
    {
        if (directory)
        {
            if (!Directory.Exists(src))
                return false;
        }
        else if (!File.Exists(src))
            return false;

        if (directory)
        {
            Files.WalkFileTree(src, new FileVisitor(
                path =>
                {
                    string dstPath;
                    try

                    {
                        dstPath = Path.Combine(dst, Path.GetRelativePath(src, path));
                    }
                    catch (ArgumentException ex)
                    {
                        throw new IOException(null, ex);
                    }

                    Directory.CreateDirectory(dstPath);
                    return FileVisitResult.CONTINUE;
                },
                path =>
                {
                    string dstPath;
                    try
                    {
                        dstPath = Path.Combine(dst, Path.GetRelativePath(src, path));
                    }
                    catch (ArgumentException ex)
                    {
                        throw new IOException(null, ex);
                    }

                    File.Copy(path, dstPath);
                    return FileVisitResult.CONTINUE;
                },
                (path, ex) =>
                {
                    if (ex != null)
                        throw ex;

                    return FileVisitResult.CONTINUE;
                },
                (path, ex) =>
                {
                    if (ex != null)
                        throw ex;

                    return FileVisitResult.CONTINUE;
                }
            ));
        }
        else
            File.Copy(src, dst);
        return true;
    }

    private static TagCompound createLevelDat(bool survival, bool night)
    {
        TagCompound dataTag = new NbtBuilder.Compound()
            .put("GameType", survival ? 0 : 1)
            .put("Difficulty", 1)
            .put("DayTime", !night ? 6000 : 18000)
            .put("GameRules", new NbtBuilder.Compound()
                .put("doDaylightCycle", "false")
                .put("doWeatherCycle", "false")
                .put("doMobSpawning", "false")
                .put("fountain:doMobDespawn", "false")
                .put("keepInventory", "true")
            )
            .put("WorldGenSettings", new NbtBuilder.Compound()
                .put("seed", (long)0)    // TODO
                .put("generate_features", (byte)0)
                .put("dimensions", new NbtBuilder.Compound()
                    .put("minecraft:overworld", new NbtBuilder.Compound()
                        .put("type", "minecraft:overworld")
                        .put("generator", new NbtBuilder.Compound()
                            .put("type", "fountain:wrapper")
                            .put("buildplate", new NbtBuilder.Compound()
                                .put("ground_level", 63))
                            .put("inner", new NbtBuilder.Compound()
                                .put("type", "minecraft:noise")
                                .put("settings", "minecraft:overworld")
                                .put("biome_source", new NbtBuilder.Compound()
                                    .put("type", "minecraft:multi_noise")
                                    .put("preset", "minecraft:overworld")
                                )
                            )
                        )
                    )
                    .put("minecraft:the_nether", new NbtBuilder.Compound()
                        .put("type", "minecraft:the_nether")
                        .put("generator", new NbtBuilder.Compound()
                            .put("type", "fountain:wrapper")
                            .put("buildplate", new NbtBuilder.Compound()
                                .put("ground_level", 32))
                            .put("inner", new NbtBuilder.Compound()
                                .put("type", "minecraft:noise")
                                .put("settings", "minecraft:nether")
                                .put("biome_source", new NbtBuilder.Compound()
                                    .put("type", "minecraft:fixed")
                                    .put("biome", "minecraft:nether_wastes")
                                )
                            )
                        )
                    )
                )
            )
            .put("DataVersion", 3700)
            .put("version", 19133)
            .put("Version", new NbtBuilder.Compound()
                .put("Id", 3700)
                .put("Name", "1.20.4")
                .put("Series", "main")
                .put("Snapshot", (byte)0)
            )
            .put("initialized", (byte)1)
            .build("Data");

        return dataTag;
    }

    private DirectoryInfo? setupBridgeFiles(byte[] serverData)
    {
        DirectoryInfo workDir = new DirectoryInfo(Path.Combine(baseDir.FullName, "bridge"));
        if (!workDir.TryCreate())
        {
            Log.Error("Could not create bridge working directory");
            return null;
        }

        // empty

        return workDir;
    }

    private void cleanupBaseDir()
    {
        Log.Information("Cleaning up runtime directory");

        try
        {
            Files.WalkFileTree(baseDir.FullName, new FileVisitor(
                path =>
                {
                    return FileVisitResult.CONTINUE;
                },
                path =>
                {
                    File.Delete(path);
                    return FileVisitResult.CONTINUE;
                },
                (path, ex) =>
                {
                    if (ex != null)
                        throw ex;

                    return FileVisitResult.CONTINUE;
                },
                (path, ex) =>
                {
                    if (ex != null)
                        throw ex;

                    Directory.Delete(path);
                    return FileVisitResult.CONTINUE;
                }
            ));
        }
        catch (IOException ex)
        {
            Log.Error($"Exception while cleaning up runtime directory: {ex}");
        }
    }

    private void startServerProcess()
    {
        Monitor.Enter(subprocessLock);

        if (shuttingDown)
        {
            Log.Debug("Already shutting down, not starting server process");
            Monitor.Exit(subprocessLock);
            return;
        }

        if (serverProcess != null)
        {
            Log.Debug("Server process has already been started");
            Monitor.Exit(subprocessLock);
            return;
        }

        Log.Information("Starting server process");

        try
        {
            bool useShellExecute = true;

            serverProcess = new ConsoleProcess(javaCmd, useShellExecute, !useShellExecute);

            if (!useShellExecute)
            {
                serverProcess.StandartTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Log.Debug($"[server] {e.Data}");
                    }
                };
                serverProcess.ErrorTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Log.Error($"[server] {e.Data}");
                    }
                };
            }

            serverProcess.ExecuteAsync(serverWorkDir!.FullName, ["-jar", fabricJarName, "-nogui"]);

            Log.Information($"Server process started, PID {serverProcess.Id}");
        }
        catch (IOException ex)
        {
            Log.Error($"Could not start server process: {ex}");
        }

        Monitor.Exit(subprocessLock);
    }

    private void startBridgeProcess()
    {
        Monitor.Enter(subprocessLock);

        if (shuttingDown)
        {
            Log.Debug("Already shutting down, not starting bridge process");
            Monitor.Exit(subprocessLock);
            return;
        }

        if (bridgeProcess != null)
        {
            Log.Debug("Bridge process has already been started");
            Monitor.Exit(subprocessLock);
            return;
        }

        Log.Information("Starting bridge process");

        try
        {
            bool useShellExecute = false;

            bridgeProcess = new ConsoleProcess(javaCmd, useShellExecute, !useShellExecute);
            if (!useShellExecute)
            {
                bridgeProcess.StandartTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Log.Debug($"[bridge] {e.Data}");
                    }
                };
                bridgeProcess.ErrorTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Log.Error($"[bridge] {e.Data}");
                    }
                };
            }

            bridgeProcess.ProcessExited += (sender, e) =>
            {
                Monitor.Enter(subprocessLock);
                if (!shuttingDown)
                {
                    Log.Warning($"Bridge process has unexpectedly terminated with exit code {bridgeProcess.ExitCode}");
                    bridgeProcess = null;
                    beginShutdown();
                }

                Monitor.Exit(subprocessLock);
            };

            bridgeProcess.ExecuteAsync(bridgeWorkDir!.FullName,
            [
                "-jar", fountainBridgeJar.FullName,
                "-port", port.ToString(),
                "-serverAddress", "127.0.0.1",
                "-serverPort", serverInternalPort.ToString(),
                "-connectorPluginJar", connectorPluginJar.FullName,
                "-connectorPluginClass", "micheal65536.vienna.buildplate.connector.plugin.ViennaConnectorPlugin",
                "-connectorPluginArg", connectorPluginArgString,
                "-useUUIDAsUsername",
            ]);

            Log.Information($"Bridge process started, PID {bridgeProcess.Id}");
        }
        catch (IOException ex)
        {
            Log.Error($"Could not start bridge process: {ex}");
        }

        Monitor.Exit(subprocessLock);
    }

#pragma warning disable IDE0022
    private void startHostPlayerConnectTimeout()
    {
        new Thread(() =>
        {
            try
            {
                Thread.Sleep(HOST_PLAYER_CONNECT_TIMEOUT);
            }
            catch (ThreadInterruptedException exception)
            {
                throw new InvalidOperationException(null, exception);
            }

            lock (subprocessLock)
            {
                if (shuttingDown)
                    return;
            }

            if (!hostPlayerConnected)
            {
                Log.Information("Host player has not connected yet, shutting down");
                beginShutdown();
            }
        }).Start();
    }

    private void startShutdownTimer()
    {
        new Thread(() =>
        {
            if (shutdownTime is { } shutdownTimeVal)
            {
                long currentTime = U.CurrentTimeMillis();
                while (currentTime < shutdownTimeVal)
                {
                    long duration = shutdownTimeVal - currentTime;
                    if (duration > 0)
                    {
                        Log.Information($"Server will shut down in {duration} milliseconds");

                        /*try
                        {*/
                        Debug.Assert((duration > 2000 ? (duration / 2) : duration) < int.MaxValue);
                        Thread.Sleep((int)(duration > 2000 ? (duration / 2) : duration));
                        /*}
                        catch (ThreadInterruptedException exception)
                        {
                            throw new AssertionError(exception);
                        }*/
                    }

                    currentTime = U.CurrentTimeMillis();
                }
            }

            Log.Information("Shutdown time has been reached, shutting down");
            beginShutdown();
        }).Start();
    }

    private void beginShutdown()
    {
        new Thread(() =>
        {
            Monitor.Enter(subprocessLock);

            if (shuttingDown)
            {
                Log.Debug("Already shutting down, not beginning shutdown");
                Monitor.Exit(subprocessLock);
                return;
            }

            shuttingDown = true;

            Log.Information("Beginning shutdown");

            this.sendEventBusInstanceStatusNotification("shuttingDown");

            if (bridgeProcess != null)
            {
                Log.Information("Waiting for bridge to shut down");
                Monitor.Exit(subprocessLock);
                bridgeProcess.StopAndWait();
                int exitCode = bridgeProcess.ExitCode;//waitForProcess(bridgeProcess.Process);
                Monitor.Enter(subprocessLock);
                bridgeProcess = null;
                Log.Information($"Bridge has finished with exit code {exitCode}");
            }

            if (serverProcess != null)
            {
                Log.Information("Asking the server to shut down");
                serverProcess.StopAndWait();
            }

            Monitor.Exit(subprocessLock);
        }).Start();
    }
#pragma warning restore IDE0022

    private static int waitForProcess(Process process)
    {
        int exitCode;
        for (; ; )
        {
            try
            {
                process.WaitForExit();
                exitCode = process.ExitCode;
                break;
            }
            catch (ThreadInterruptedException)
            {
                continue;
            }
        }

        return exitCode;
    }

    public void waitForShutdown()
    {
        for (; ; )
        {
            try
            {
                if (thread is null)
                {
                    Log.Debug("thread is null in waitForShutdown");
                    continue;
                }

                thread.Join();
                break;
            }
            catch (ThreadInterruptedException)
            {
                continue;
            }
        }
    }

    private sealed record BuildplateLoadRequest(
        string playerId,
        string buildplateId
    );

    private sealed record EncounterBuildplateLoadRequest(
        string encounterBuildplateId
    );

    private sealed record SharedBuildplateLoadRequest(
        string sharedBuildplateId
    );

    private sealed record BuildplateLoadResponse(
        string serverDataBase64
    );

    [JsonConverter(typeof(StringEnumConverter))]
    public enum BuildplateSource
    {
        PLAYER,
        SHARED,
        ENCOUNTER
    }
}
