using Cyotek.Data.Nbt;
using Cyotek.Data.Nbt.Serialization;
using Serilog;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;
using ViennaDotNet.Buildplate.Connector.Model;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Buildplate.Connector.Model;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.Buildplate.Launcher;

public class Instance
{
    private const int HOST_PLAYER_CONNECT_TIMEOUT = 60_000;

    public static Instance Run(EventBusClient eventBusClient, string? playerId, string buildplateId, BuildplateSource buildplateSource, string instanceId, bool survival, bool night, bool saveEnabled, InventoryType inventoryType, long? shutdownTime, string publicAddress, int port, int serverInternalPort, string javaCmd, FileInfo fountainBridgeJar, DirectoryInfo serverTemplateDir, string fabricJarName, FileInfo connectorPluginJar, DirectoryInfo baseDir, string eventBusConnectionstring)
    {
        if (playerId is null && buildplateSource == BuildplateSource.PLAYER)
        {
            throw new ArgumentException($"{nameof(playerId)} was not while {nameof(buildplateSource)} was {nameof(BuildplateSource.PLAYER)}.", nameof(playerId));
        }

        var instance = new Instance(eventBusClient, playerId, buildplateId, buildplateSource, instanceId, survival, night, saveEnabled, inventoryType, shutdownTime, publicAddress, port, serverInternalPort, javaCmd, fountainBridgeJar, serverTemplateDir, fabricJarName, connectorPluginJar, baseDir, eventBusConnectionstring);

        instance._threadStartedSemaphore.Wait();
        new Thread(instance.Run)
        {
            Name = $"Instance {instanceId}"
        }.Start();
        instance._threadStartedSemaphore.Wait();
        instance._threadStartedSemaphore.Release();

        return instance;
    }

    private readonly EventBusClient _eventBusClient;

    private readonly string? _playerId;
    private readonly string _buildplateId;
    private readonly BuildplateSource _buildplateSource;
    public readonly string InstanceId;
    private readonly bool _survival;
    private readonly bool _night;
    private readonly bool _saveEnabled;
    private readonly InventoryType _inventoryType;
    private readonly long? _shutdownTime;

    public readonly string PublicAddress;
    public readonly int Port;
    private readonly int _serverInternalPort;

    private readonly string _javaCmd;
    private readonly FileInfo _fountainBridgeJar;
    private readonly DirectoryInfo _serverTemplateDir;
    private readonly string _fabricJarName;
    private readonly FileInfo _connectorPluginJar;
    private readonly DirectoryInfo _baseDir;
    private readonly string _eventBusAddress;
    private readonly string _eventBusQueueName;
    private readonly string _connectorPluginArgString;

    private Thread? _thread;
    private readonly SemaphoreSlim _threadStartedSemaphore = new SemaphoreSlim(1, 1);

    private Publisher? _publisher = null;
    private RequestSender? _requestSender = null;

    private Subscriber? _subscriber = null;
    private RequestHandler? _requestHandler = null;

    private DirectoryInfo? _serverWorkDir;
    private DirectoryInfo? _bridgeWorkDir;
    private ConsoleProcess? _serverProcess = null;
    private ConsoleProcess? _bridgeProcess = null;
    private bool _shuttingDown = false;
    private readonly Lock _subprocessLock = new();

    private volatile bool _hostPlayerConnected = false;

    private Instance(EventBusClient eventBusClient, string? playerId, string buildplateId, BuildplateSource buildplateSource, string instanceId, bool survival, bool night, bool saveEnabled, InventoryType inventoryType, long? shutdownTime, string publicAddress, int port, int serverInternalPort, string javaCmd, FileInfo fountainBridgeJar, DirectoryInfo serverTemplateDir, string fabricJarName, FileInfo connectorPluginJar, DirectoryInfo baseDir, string eventBusConnectionString)
    {
        _eventBusClient = eventBusClient;

        _playerId = playerId;
        _buildplateId = buildplateId;
        _buildplateSource = buildplateSource;
        InstanceId = instanceId;
        _survival = survival;
        _night = night;
        _saveEnabled = saveEnabled;
        _inventoryType = inventoryType;
        _shutdownTime = shutdownTime;

        PublicAddress = publicAddress;
        Port = port;
        _serverInternalPort = serverInternalPort;

        _javaCmd = javaCmd;
        _fountainBridgeJar = fountainBridgeJar;
        _serverTemplateDir = serverTemplateDir;
        _fabricJarName = fabricJarName;
        _connectorPluginJar = connectorPluginJar;
        _baseDir = baseDir;
        _eventBusAddress = eventBusConnectionString;
        _eventBusQueueName = "buildplate_" + InstanceId;
        _connectorPluginArgString = Json.Serialize(new ConnectorPluginArg(_eventBusAddress, _eventBusQueueName, _inventoryType));
    }

    private void Run()
    {
        _thread = Thread.CurrentThread;
        _threadStartedSemaphore.Release();

        try
        {
            Log.Information(_buildplateSource switch
            {
                BuildplateSource.PLAYER => $"Starting for player {_playerId} buildplate {_buildplateId} (survival = {_survival}, saveEnabled = {_saveEnabled}, inventoryType = {_inventoryType})",
                BuildplateSource.SHARED => $"Starting for shared buildplate {_buildplateId} (player = {_playerId}, survival = {_survival}, saveEnabled = {_saveEnabled}, inventoryType = {_inventoryType})",
                BuildplateSource.ENCOUNTER => $"Starting for encounter buildplate {_buildplateId} (player = {_playerId}, survival = {_survival}, saveEnabled = {_saveEnabled}, inventoryType = {_inventoryType})",
                _ => throw new UnreachableException(),
            });
            Log.Information($"Using port {Port} internal port {_serverInternalPort}");

            _publisher = _eventBusClient.AddPublisher();
            _requestSender = _eventBusClient.AddRequestSender();

            Log.Information("Setting up server");

            BuildplateLoadResponse? buildplateLoadResponse = _buildplateSource switch
            {
                BuildplateSource.PLAYER => SendEventBusRequestRaw<BuildplateLoadResponse>("load", new BuildplateLoadRequest(_playerId!, _buildplateId), true).Result,
                BuildplateSource.SHARED => SendEventBusRequestRaw<BuildplateLoadResponse>("loadShared", new SharedBuildplateLoadRequest(_buildplateId), true).Result,
                BuildplateSource.ENCOUNTER => SendEventBusRequestRaw<BuildplateLoadResponse>("loadEncounter", new EncounterBuildplateLoadRequest(_buildplateId), true).Result,
                _ => throw new UnreachableException(),
            };

            Debug.Assert(buildplateLoadResponse is not null);

            byte[] serverData;
            try
            {
                serverData = Convert.FromBase64String(buildplateLoadResponse.ServerDataBase64);
            }
            catch
            {
                Log.Error("Buildplate load response contained invalid base64 data");
                return;
            }

            try
            {
                _serverWorkDir = SetupServerFiles(serverData);
                if (_serverWorkDir is null)
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
                _bridgeWorkDir = SetupBridgeFiles(serverData);
                if (_bridgeWorkDir is null)
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

            _subscriber = _eventBusClient.AddSubscriber(_eventBusQueueName, new Subscriber.SubscriberListener(
                HandleConnectorEvent,
                () =>
                {
                    Log.Error("Event bus subscriber error");
                    BeginShutdown();
                }
            ));
            _requestHandler = _eventBusClient.AddRequestHandler(_eventBusQueueName, new RequestHandler.Handler(
                async request =>
                {
                    object? responseObject = await HandleConnectorRequest(request);
                    return responseObject is not null ? Json.Serialize(responseObject) : null;
                },
                () =>
                {
                    Log.Error("Event bus request handler error");
                    BeginShutdown();
                }
            ));

            _subprocessLock.Enter();
            if (!_shuttingDown)
            {
                StartServerProcess();
                if (_serverProcess is not null)
                {
                    _subprocessLock.Exit();
                    int exitCode = WaitForProcess(_serverProcess.Process);
                    _subprocessLock.Enter();
                    _serverProcess = null;
                    if (!_shuttingDown)
                    {
                        Log.Warning($"Server process has unexpectedly terminated with exit code {exitCode}");
                    }
                    else
                    {
                        Log.Information($"Server has finished with exit code {exitCode}");
                    }

                    _shuttingDown = true;

                    if (_bridgeProcess is not null)
                    {
                        Log.Information("Bridge is still running, shutting it down now");
                        _bridgeProcess.StopAndWait();
                        _subprocessLock.Exit();
                        exitCode = WaitForProcess(_bridgeProcess.Process);
                        _subprocessLock.Enter();
                        _bridgeProcess = null;
                        Log.Information($"Bridge has finished with exit code {exitCode}");
                    }
                }
                else
                {
                    Log.Information("Server failed to start");
                }
            }

            _subprocessLock.Exit();
        }
        catch (Exception ex)
        {
            Log.Error($"Unhandled exception: {ex}");
        }
        finally
        {
            _subscriber?.Close();

            _requestHandler?.Close();

            if (_publisher is not null)
            {
                _publisher.Flush();
                _publisher.Close();
            }

            if (_requestSender is not null)
            {
                _requestSender.Flush();
                _requestSender.Close();
            }

            CleanupBaseDir();

            Log.Information("Finished");
        }
    }

    private async Task HandleConnectorEvent(Subscriber.Event @event)
    {
        switch (@event.Type)
        {
            case "started":
                {
                    Log.Information("Server is ready");
                    StartBridgeProcess();
                    SendEventBusInstanceStatusNotification("ready");
                    if (_shutdownTime is not null)
                    {
                        StartShutdownTimer();
                    }
                    else
                    {
                        StartHostPlayerConnectTimeout();
                    }
                }

                break;
            case "saved":
                {
                    if (_saveEnabled)
                    {
                        WorldSavedMessage? worldSavedMessage = ReadJson<WorldSavedMessage>(@event.Data);
                        if (worldSavedMessage is not null)
                        {
                            if (_hostPlayerConnected)

                            {
                                Log.Information("Saving snapshot");
                                SendEventBusRequest<object>("saved", worldSavedMessage, false)
                                    .Forget(ex =>
                                    {
                                        Log.Error($"Failed to send event bus request 'saved': {ex}");
                                    });
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
                    Log.Information("**************AddItem**************");
                    InventoryAddItemMessage? inventoryAddItemMessage = ReadJson<InventoryAddItemMessage>(@event.Data);
                    if (inventoryAddItemMessage is not null)
                    {
                        await SendEventBusRequest<object>("inventoryAdd", inventoryAddItemMessage, false);
                        Log.Information("**************AddItem null json**************");
                    }
                }

                break;
            case "inventoryUpdateWear":
                {
                    InventoryUpdateItemWearMessage? inventoryUpdateItemWearMessage = ReadJson<InventoryUpdateItemWearMessage>(@event.Data);
                    if (inventoryUpdateItemWearMessage is not null)
                        await SendEventBusRequest<object>("inventoryUpdateWear", inventoryUpdateItemWearMessage, false);
                }

                break;
            case "inventorySetHotbar":
                {
                    Log.Information("**************SetHotbar**************");
                    InventorySetHotbarMessage? inventorySetHotbarMessage = ReadJson<InventorySetHotbarMessage>(@event.Data);
                    if (inventorySetHotbarMessage is not null)
                    {
                        Log.Information("**************SetHotbar null response**************");
                        await SendEventBusRequest<object>("inventorySetHotbar", inventorySetHotbarMessage, false);
                    }
                }

                break;
            default:
                Log.Warning($"Unkown connector event: {@event.Type}");
                break;
        }
    }

    private async Task<object?> HandleConnectorRequest(RequestHandler.Request request)
    {
        switch (request.Type)
        {
            case "playerConnected":
                {
                    PlayerConnectedRequest? playerConnectedRequest = ReadJson<PlayerConnectedRequest>(request.Data);
                    if (playerConnectedRequest is not null)
                    {
                        if (_playerId is not null && !_hostPlayerConnected && playerConnectedRequest.Uuid != _playerId)
                        {
                            Log.Information($"Rejecting player connection for player {playerConnectedRequest.Uuid} because the host player must connect first");
                            return new PlayerConnectedResponse(false, null);
                        }

                        PlayerConnectedResponse? playerConnectedResponse = await SendEventBusRequest<PlayerConnectedResponse>("playerConnected", playerConnectedRequest, true);
                        if (playerConnectedResponse is not null)
                        {
                            Log.Information($"Player {playerConnectedRequest.Uuid} has connected");

                            if (_playerId is not null && !_hostPlayerConnected && playerConnectedRequest.Uuid == _playerId)
                            {
                                _hostPlayerConnected = true;
                            }

                            return playerConnectedResponse;
                        }
                    }
                }

                break;
            case "playerDisconnected":
                {
                    Log.Debug("Player dicconnecting...");
                    PlayerDisconnectedRequest? playerDisconnectedRequest = ReadJson<PlayerDisconnectedRequest>(request.Data);
                    if (playerDisconnectedRequest is not null)
                    {
                        PlayerDisconnectedResponse? playerDisconnectedResponse = await SendEventBusRequest<PlayerDisconnectedResponse>("playerDisconnected", playerDisconnectedRequest, true);
                        if (playerDisconnectedResponse is not null)
                        {
                            Log.Information($"Player {playerDisconnectedRequest.PlayerId} has disconnected");

                            if (_shutdownTime is null && _playerId is not null && playerDisconnectedRequest.PlayerId == _playerId)
                            {
                                Log.Information("Host player has disconnected, beginning shutdown");
                                BeginShutdown();
                            }

                            return playerDisconnectedResponse;
                        }
                    }
                    else
                    {
                        Log.Information("**************playerDisconnected null json**************");
                    }
                }

                break;
            case "playerDead":
                {
                    string? playerId = ReadJson<string>(request.Data);
                    if (playerId is not null)
                    {
                        bool? respawn = await SendEventBusRequest<bool?>("playerDead", playerId, true);
                        if (respawn is not null)
                        {
                            return respawn.Value;
                        }
                        else
                        {
                            Log.Information("**************playerDead null response**************");
                        }
                    }
                    else
                    {
                        Log.Information("**************playerDead null json**************");
                    }
                }

                break;
            case "getInventory":
                {
                    string? playerId = ReadJson<string>(request.Data);
                    Log.Information("**************GetInventory**************");
                    if (playerId is not null)
                    {
                        InventoryResponse? inventoryResponse = await SendEventBusRequest<InventoryResponse>("getInventory", playerId, true);

                        if (inventoryResponse is not null)
                        {
                            return inventoryResponse;
                        }
                        else
                        {
                            Log.Information("**************GetInventory response null**************");
                        }
                    }
                    else
                    {
                        Log.Warning("**************GetInventory player null**************");
                    }
                }

                break;
            case "inventoryRemove":
                {
                    InventoryRemoveItemRequest? inventoryRemoveItemRequest = ReadJson<InventoryRemoveItemRequest>(request.Data);
                    Log.Information("**************InventoryRemove**************");
                    if (inventoryRemoveItemRequest is not null)
                    {
                        if (inventoryRemoveItemRequest.InstanceId is not null)
                        {
                            bool? success = await SendEventBusRequest<bool>("inventoryRemove", inventoryRemoveItemRequest, true);

                            if (success is not null)
                            {
                                return success;
                            }
                            else
                            {
                                Log.Information("**************inventoryRemove response null 1**************");
                            }
                        }
                        else
                        {
                            int? removedCount = await SendEventBusRequest<int>("inventoryRemove", inventoryRemoveItemRequest, true);
                            if (removedCount is not null)
                            {
                                return removedCount;
                            }
                            else
                            {
                                Log.Information("**************inventoryRemove response null 2**************");
                            }
                        }
                    }
                    else
                    {
                        Log.Warning("**************InventoryRemove request null**************");
                    }
                }

                break;
            case "findPlayer":
                {
                    FindPlayerIdRequest? findPlayerIdRequest = ReadJson<FindPlayerIdRequest>(request.Data);
                    if (findPlayerIdRequest is not null)
                    {
                        // TODO
                        return findPlayerIdRequest.MinecraftName;
                    }
                }

                break;
            case "getInitialPlayerState":
                {
                    string? playerId = ReadJson<string>(request.Data);
                    if (playerId is not null)
                    {
                        InitialPlayerStateResponse? initialPlayerStateResponse = await SendEventBusRequest<InitialPlayerStateResponse>("getInitialPlayerState", playerId, true);
                        if (initialPlayerStateResponse is not null)
                        {
                            return initialPlayerStateResponse;
                        }
                    }
                }

                break;
            default:
                Log.Warning($"Unkown connector request: {request.Type}");
                break;
        }

        return null;
    }

    private T? ReadJson<T>(string str)
    {
        try
        {
            return Json.Deserialize<T>(str);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to decode event bus message JSON: {ex}");
            BeginShutdown();
            return default;
        }
    }

    private sealed record RequestWithInstanceId(
        string InstanceId,
        object Request
    );

    private void SendEventBusInstanceStatusNotification(string status)
    {
        Debug.Assert(_publisher is not null);

        _publisher.Publish("buildplates", status, InstanceId).ContinueWith(task =>
        {
            if (!task.Result)
            {
                Log.Error("Event bus publisher error");
                BeginShutdown();
            }
        });
    }

    private Task<T?> SendEventBusRequest<T>(string type, object obj, bool returnResponse)
    {
        var request = new RequestWithInstanceId(InstanceId, obj);

        return SendEventBusRequestRaw<T>(type, request, returnResponse);
    }

    private async Task<T?> SendEventBusRequestRaw<T>(string type, object obj, bool returnResponse)
    {
        try
        {
            string? response = await _requestSender!.Request("buildplates", type, Json.Serialize(obj)).Task;

            if (response is null)
            {
                Log.Error("Event bus request failed (no response)");
                BeginShutdown();
                return default;
            }

            return returnResponse
                ? Json.Deserialize<T>(response)
                : default;
        }
        catch (Exception ex)
        {
            Log.Error($"Event bus request failed: {ex}");
            BeginShutdown();
            return default;
        }
    }

    private DirectoryInfo? SetupServerFiles(byte[] serverData)
    {
        var workDir = new DirectoryInfo(Path.Combine(_baseDir.FullName, "server"));
        if (!workDir.TryCreate())
        {
            Log.Error("Could not create server working directory");
            return null;
        }

        if (!CopyServerFile(Path.Combine(_serverTemplateDir.FullName, _fabricJarName), Path.Combine(workDir.FullName, _fabricJarName), false))
        {

            Log.Error($"Fabric JAR {_fabricJarName} does not exist in server template directory");
            return null;
        }

        bool warnedMissingServerFiles = false;
        if (!CopyServerFile(Path.Combine(Path.Combine(_serverTemplateDir.FullName, ".fabric"), "server"), Path.Combine(workDir.FullName, ".fabric/server"), true))
        {
            if (!warnedMissingServerFiles)
            {

                Log.Warning("Server files were not pre-downloaded in server template directory, it is recommended to pre-download all server files to improve instance start-up time and reduce network data usage");
                warnedMissingServerFiles = true;
            }
        }

        if (!CopyServerFile(Path.Combine(_serverTemplateDir.FullName, "libraries"), Path.Combine(workDir.FullName, "libraries"), true))
        {
            if (!warnedMissingServerFiles)
            {
                Log.Warning("Server files were not pre-downloaded in server template directory, it is recommended to pre-download all server files to improve instance start-up time and reduce network data usage");
                warnedMissingServerFiles = true;
            }
        }

        if (!CopyServerFile(Path.Combine(_serverTemplateDir.FullName, "versions"), Path.Combine(workDir.FullName, "versions"), true))
        {
            if (!warnedMissingServerFiles)
            {
                Log.Warning("Server files were not pre-downloaded in server template directory, it is recommended to pre-download all server files to improve instance start-up time and reduce network data usage");
                warnedMissingServerFiles = true;
            }
        }

        if (!CopyServerFile(Path.Combine(_serverTemplateDir.FullName, "mods"), Path.Combine(workDir.FullName, "mods"), true))
        {
            Log.Error("Mods directory was not present in server template directory, the buildplate server instance will not function correctly without the Fountain and Vienna Fabric mods installed");
        }

        File.WriteAllText(Path.Combine(workDir.FullName, "eula.txt"), "eula=true");

        string serverProperties = new StringBuilder()
            .Append("online-mode=false\n")
            .Append("enforce-secure-profile=false\n")
            .Append("sync-chunk-writes=false\n")
            .Append("spawn-protection=0\n")
            .Append($"server-port={_serverInternalPort}\n")
            .Append($"gamemode={(_survival ? "survival" : "creative")}\n")
            .Append($"vienna-event-bus-address={_eventBusAddress}\n")
            .Append($"vienna-event-bus-queue-name={_eventBusQueueName}\n")
            .ToString();
        File.WriteAllText(Path.Combine(workDir.FullName, "server.properties"), serverProperties);

        var worldDir = new DirectoryInfo(Path.Combine(workDir.FullName, "world"));
        if (!worldDir.TryCreate())
        {
            Log.Error("Could not create server world directory");
            return null;
        }

        var worldEntitiesDir = new DirectoryInfo(Path.Combine(worldDir.FullName, "entities"));
        if (!worldEntitiesDir.TryCreate())
        {
            Log.Error("Could not create server world entities directory");
            return null;
        }

        var worldRegionDir = new DirectoryInfo(Path.Combine(worldDir.FullName, "region"));
        if (!worldRegionDir.TryCreate())
        {
            Log.Error("Could not create server world regions directory");
            return null;
        }

        TagCompound levelDatTag = CreateLevelDat(_survival, _night);
        using (var fs = new FileStream(Path.Combine(worldDir.FullName, "level.dat"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
        using (var gzs = new GZipStream(fs, CompressionLevel.Optimal))
        {
            var writer = new BinaryTagWriter(gzs);
            writer.WriteStartDocument();
            writer.WriteStartTag(null, TagType.Compound);
            writer.WriteTag(levelDatTag);
            writer.WriteEndTag();
            writer.WriteEndDocument();
        }
        //NBTIO.writeFile(levelDatTag, new File(worldDir, "level.dat"));

        using (var byteArrayInputStream = new MemoryStream(serverData))
        using (var zipInputStream = new ZipArchive(byteArrayInputStream))
        {
            foreach (ZipArchiveEntry entry in zipInputStream.Entries)
            {
                if (entry.IsDirectory())
                {
                    continue;
                }

                string path = Path.Combine(worldDir.FullName, entry.FullName);

                using (Stream zipStream = entry.Open())
                using (FileStream fs = File.OpenWrite(path))
                {
                    zipStream.CopyTo(fs);
                }
            }
        }

        return workDir;
    }

    private static bool CopyServerFile(string src, string dst, bool directory)
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
                    return ex is not null
                        ? throw ex
                        : FileVisitResult.CONTINUE;
                },
                (path, ex) =>
                {
                    return ex is not null
                        ? throw ex
                        : FileVisitResult.CONTINUE;
                }
            ));
        }
        else
            File.Copy(src, dst);
        return true;
    }

    private static TagCompound CreateLevelDat(bool survival, bool night)
    {
        TagCompound dataTag = new NbtBuilder.Compound()
            .Add("GameType", survival ? 0 : 1)
            .Add("Difficulty", 1)
            .Add("DayTime", !night ? 6000 : 18000)
            .Add("GameRules", new NbtBuilder.Compound()
                .Add("doDaylightCycle", "false")
                .Add("doWeatherCycle", "false")
                .Add("doMobSpawning", "false")
                .Add("fountain:doMobDespawn", "false")
                .Add("keepInventory", "true")
            )
            .Add("WorldGenSettings", new NbtBuilder.Compound()
                .Add("seed", (long)0)    // TODO
                .Add("generate_features", (byte)0)
                .Add("dimensions", new NbtBuilder.Compound()
                    .Add("minecraft:overworld", new NbtBuilder.Compound()
                        .Add("type", "minecraft:overworld")
                        .Add("generator", new NbtBuilder.Compound()
                            .Add("type", "fountain:wrapper")
                            .Add("buildplate", new NbtBuilder.Compound()
                                .Add("ground_level", 63))
                            .Add("inner", new NbtBuilder.Compound()
                                .Add("type", "minecraft:noise")
                                .Add("settings", "minecraft:overworld")
                                .Add("biome_source", new NbtBuilder.Compound()
                                    .Add("type", "minecraft:multi_noise")
                                    .Add("preset", "minecraft:overworld")
                                )
                            )
                        )
                    )
                    .Add("minecraft:the_nether", new NbtBuilder.Compound()
                        .Add("type", "minecraft:the_nether")
                        .Add("generator", new NbtBuilder.Compound()
                            .Add("type", "fountain:wrapper")
                            .Add("buildplate", new NbtBuilder.Compound()
                                .Add("ground_level", 32))
                            .Add("inner", new NbtBuilder.Compound()
                                .Add("type", "minecraft:noise")
                                .Add("settings", "minecraft:nether")
                                .Add("biome_source", new NbtBuilder.Compound()
                                    .Add("type", "minecraft:fixed")
                                    .Add("biome", "minecraft:nether_wastes")
                                )
                            )
                        )
                    )
                )
            )
            .Add("DataVersion", 3700)
            .Add("version", 19133)
            .Add("Version", new NbtBuilder.Compound()
                .Add("Id", 3700)
                .Add("Name", "1.20.4")
                .Add("Series", "main")
                .Add("Snapshot", (byte)0)
            )
            .Add("initialized", (byte)1)
            .Build("Data");

        return dataTag;
    }

    private DirectoryInfo? SetupBridgeFiles(byte[] serverData)
    {
        var workDir = new DirectoryInfo(Path.Combine(_baseDir.FullName, "bridge"));
        if (!workDir.TryCreate())
        {
            Log.Error("Could not create bridge working directory");
            return null;
        }

        // empty

        return workDir;
    }

    private void CleanupBaseDir()
    {
        Log.Information("Cleaning up runtime directory");

        Log.Information("Skipped");
        return;

        try
        {
            Files.WalkFileTree(_baseDir.FullName, new FileVisitor(
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
                    return ex is not null
                        ? throw ex
                        : FileVisitResult.CONTINUE;
                },
                (path, ex) =>
                {
                    if (ex is not null)
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

    private void StartServerProcess()
    {
        lock (_subprocessLock)
        {
            if (_shuttingDown)
            {
                Log.Debug("Already shutting down, not starting server process");
                return;
            }

            if (_serverProcess is not null)
            {
                Log.Debug("Server process has already been started");
                return;
            }

            Log.Information("Starting server process");

            try
            {
                bool openInNewWindow = true;

                _serverProcess = new ConsoleProcess(_javaCmd, openInNewWindow, !openInNewWindow, openInNewWindow: openInNewWindow);

                if (!openInNewWindow)
                {
                    _serverProcess.StandartTextReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            Log.Debug($"[server] {e.Data}");
                        }
                    };
                    _serverProcess.ErrorTextReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            Log.Error($"[server] {e.Data}");
                        }
                    };
                }

                _serverProcess.ExecuteAsync(_serverWorkDir!.FullName, ["-jar", _fabricJarName, "-nogui"]);

                Log.Information($"Server process started, PID {_serverProcess.Id}");
            }
            catch (IOException ex)
            {
                Log.Error($"Could not start server process: {ex}");
            }
        }
    }

    private void StartBridgeProcess()
    {
        lock (_subprocessLock)
        {
            if (_shuttingDown)
            {
                Log.Debug("Already shutting down, not starting bridge process");
                return;
            }

            if (_bridgeProcess is not null)
            {
                Log.Debug("Bridge process has already been started");
                return;
            }

            Log.Information("Starting bridge process");

            try
            {
                bool openInNewWindow = true;

                _bridgeProcess = new ConsoleProcess(_javaCmd, openInNewWindow, !openInNewWindow, openInNewWindow: openInNewWindow);
                if (!openInNewWindow)
                {
                    _bridgeProcess.StandartTextReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            Log.Debug($"[bridge] {e.Data}");
                        }
                    };
                    _bridgeProcess.ErrorTextReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            Log.Error($"[bridge] {e.Data}");
                        }
                    };
                }

                _bridgeProcess.ProcessExited += (sender, e) =>
                {
                    lock (_subprocessLock)
                    {
                        if (!_shuttingDown)
                        {
                            Log.Warning($"Bridge process has unexpectedly terminated with exit code {_bridgeProcess.ExitCode}");
                            _bridgeProcess = null;
                            BeginShutdown();
                        }
                    }
                };

                _bridgeProcess.ExecuteAsync(_bridgeWorkDir!.FullName,
                [
                    "-jar", _fountainBridgeJar.FullName,
                    "-port", Port.ToString(),
                    "-serverAddress", "127.0.0.1",
                    "-serverPort", _serverInternalPort.ToString(),
                    "-connectorPluginJar", _connectorPluginJar.FullName,
                    "-connectorPluginClass", "micheal65536.vienna.buildplate.connector.plugin.ViennaConnectorPlugin",
                    "-connectorPluginArg", _connectorPluginArgString,
                    "-useUUIDAsUsername",
                ]);

                Log.Information($"Bridge process started, PID {_bridgeProcess.Id}");
            }
            catch (IOException ex)
            {
                Log.Error($"Could not start bridge process: {ex}");
            }
        }
    }

#pragma warning disable IDE0022
    private void StartHostPlayerConnectTimeout()
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

            lock (_subprocessLock)
            {
                if (_shuttingDown)
                {
                    return;
                }
            }

            if (!_hostPlayerConnected)
            {
                Log.Information("Host player has not connected yet, shutting down");
                BeginShutdown();
            }
        }).Start();
    }

    private void StartShutdownTimer()
    {
        new Thread(() =>
        {
            if (_shutdownTime is { } shutdownTimeVal)
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
            BeginShutdown();
        }).Start();
    }

    private void BeginShutdown()
    {
        // a "bit" ugly
        ((Func<Task>)(async () =>
        {
            await Task.Yield();

            _subprocessLock.Enter();

            if (_shuttingDown)
            {
                Log.Debug("Already shutting down, not beginning shutdown");
                _subprocessLock.Exit();
                return;
            }

            _shuttingDown = true;

            Log.Information("Beginning shutdown");

            SendEventBusInstanceStatusNotification("shuttingDown");

            if (_bridgeProcess is not null)
            {
                Log.Information("Waiting for bridge to shut down");
                _subprocessLock.Exit();
                _bridgeProcess.StopAndWait();
                int exitCode = _bridgeProcess.ExitCode;//waitForProcess(bridgeProcess.Process);
                _subprocessLock.Enter();
                _bridgeProcess = null;
                Log.Information($"Bridge has finished with exit code {exitCode}");
            }

            if (_serverProcess is not null)
            {
                Log.Information("Asking the server to shut down");
                _serverProcess.StopAndWait();
            }

            _subprocessLock.Exit();
        }))().Forget(ex =>
        {
            Log.Error(ex.Message);
        });
    }
#pragma warning restore IDE0022

    private static int WaitForProcess(Process process)
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

    public void WaitForShutdown()
    {
        while (true)
        {
            try
            {
                if (_thread is null)
                {
                    Log.Debug("thread is null in waitForShutdown");
                    continue;
                }

                _thread.Join();
                break;
            }
            catch (ThreadInterruptedException)
            {
                continue;
            }
        }
    }

    private sealed record BuildplateLoadRequest(
        string PlayerId,
        string BuildplateId
    );

    private sealed record EncounterBuildplateLoadRequest(
        string EncounterBuildplateId
    );

    private sealed record SharedBuildplateLoadRequest(
        string SharedBuildplateId
    );

    private sealed record BuildplateLoadResponse(
        string ServerDataBase64
    );

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BuildplateSource
    {
        PLAYER,
        SHARED,
        ENCOUNTER
    }
}
