using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;
using Cyotek.Data.Nbt;
using Cyotek.Data.Nbt.Serialization;
using Serilog;
using Solace.Buildplate.Connector.Model;
using Solace.Common;
using Solace.Common.Utils;
using Solace.EventBus.Client;

namespace Solace.Buildplate.Launcher;

#pragma warning disable CA1001 // Types that own disposable fields should be disposable
public sealed class Instance
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
{
    private const long HOST_PLAYER_CONNECT_TIMEOUT = 120_000;

    public static Instance Run(EventBusClient eventBusClient, string? playerId, string buildplateId, BuildplateSource buildplateSource, string instanceId, bool survival, bool night, bool saveEnabled, InventoryType inventoryType, long? shutdownTime, string publicAddress, int port, int serverInternalPort, string javaCmd, FileInfo fountainBridgeJar, DirectoryInfo serverTemplateDir, string fabricJarName, FileInfo connectorPluginJar, DirectoryInfo baseDir, string eventBusConnectionString)
    {
        if (playerId is null && buildplateSource is BuildplateSource.PLAYER)
        {
            throw new ArgumentException($"{nameof(playerId)} cannot be null when {nameof(buildplateSource)} is {nameof(BuildplateSource.PLAYER)}");
        }

        var instance = new Instance(eventBusClient, playerId, buildplateId, buildplateSource, instanceId, survival, night, saveEnabled, inventoryType, shutdownTime, publicAddress, port, serverInternalPort, javaCmd, fountainBridgeJar, serverTemplateDir, fabricJarName, connectorPluginJar, baseDir, eventBusConnectionString);
        instance._threadStartedSemaphore.Wait();
        instance._thread = instance.RunAsync();
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

    private Task? _thread;
    private readonly SemaphoreSlim _threadStartedSemaphore = new SemaphoreSlim(1, 1);
    private readonly ILogger _logger;

    private Publisher? _publisher;
    private RequestSender? _requestSender;

    private Subscriber? _subscriber;
    private RequestHandler? _requestHandler;

    private DirectoryInfo _serverWorkDir = null!;
    private DirectoryInfo _bridgeWorkDir = null!;
    private ConsoleProcess? _serverProcess;
    private ConsoleProcess? _bridgeProcess;
    private bool _shuttingDown;
    private readonly ReentrantAsyncLock.ReentrantAsyncLock _subprocessLock = new ReentrantAsyncLock.ReentrantAsyncLock(); // java uses ReentrantLock, Lock cannot be used, because it does not support locking and unlocking on different threads, which happens due to async, SemaphoreSlim does not support multiple locks from the same async context

    private volatile bool _hostPlayerConnected;

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
        _connectorPluginArgString = Json.Serialize(new ConnectorPluginArg(
            _eventBusAddress,
            _eventBusQueueName,
            _inventoryType
        ));

        _logger = Log.Logger.ForContext("InstanceId", InstanceId);
    }

    private async Task RunAsync()
    {
        await Task.Yield();

        _threadStartedSemaphore.Release();

        try
        {
            switch (_buildplateSource)
            {
                case BuildplateSource.PLAYER:
                    _logger.Information($"Starting for player {_playerId} buildplate {_buildplateId} (survival = {_survival}, saveEnabled = {_saveEnabled}, inventoryType = {_inventoryType})");
                    break;
                case BuildplateSource.SHARED:
                    _logger.Information($"Starting for shared buildplate {_buildplateId} (player = {_playerId}, survival = {_survival}, saveEnabled = {_saveEnabled}, inventoryType = {_inventoryType})");
                    break;
                case BuildplateSource.ENCOUNTER:
                    _logger.Information($"Starting for encounter buildplate {_buildplateId} (player = {_playerId}, survival = {_survival}, saveEnabled = {_saveEnabled}, inventoryType = {_inventoryType})");
                    break;
            }

            _logger.Information($"Using port {Port} internal port {_serverInternalPort}");

            _publisher = await _eventBusClient.AddPublisherAsync();
            _requestSender = await _eventBusClient.AddRequestSenderAsync();

            _logger.Information("Setting up server");

            BuildplateLoadResponse? buildplateLoadResponse = _buildplateSource switch
            {
                BuildplateSource.PLAYER => await SendEventBusRequestRaw<BuildplateLoadResponse>("load", new BuildplateLoadRequest(_playerId!, _buildplateId), true),
                BuildplateSource.SHARED => await SendEventBusRequestRaw<BuildplateLoadResponse>("loadShared", new SharedBuildplateLoadRequest(_buildplateId), true),
                BuildplateSource.ENCOUNTER => await SendEventBusRequestRaw<BuildplateLoadResponse>("loadEncounter", new EncounterBuildplateLoadRequest(_buildplateId), true),
                _ => throw new UnreachableException(),
            };

            Debug.Assert(buildplateLoadResponse is not null);

            byte[] serverData;
            try
            {
                serverData = Convert.FromBase64String(buildplateLoadResponse.ServerDataBase64);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Buildplate load response contained invalid base64 data");
                return;
            }

            try
            {
                var serverWorkDir = await SetupServerFiles(serverData);
                if (serverWorkDir is null)
                {
                    _logger.Error("Could not set up files for server");
                    return;
                }

                _serverWorkDir = serverWorkDir;
            }
            catch (IOException exception)
            {
                _logger.Error(exception, "Could not set up files for server");
                return;
            }

            try
            {
                var bridgeWorkDir = SetupBridgeFiles(serverData);
                if (bridgeWorkDir is null)
                {
                    _logger.Error("Could not set up files for bridge");
                    return;
                }

                _bridgeWorkDir = bridgeWorkDir;
            }
            catch (IOException exception)
            {
                _logger.Error(exception, "Could not set up files for bridge");
                return;
            }

            _logger.Information("Running server");

            _subscriber = await _eventBusClient.AddSubscriberAsync(_eventBusQueueName, new SubscriberListener(
                HandleConnectorEvent,
                async () =>
                {
                    _logger.Error("Event bus subscriber error");
                    BeginShutdown();
                }
            ));

            _requestHandler = await _eventBusClient.AddRequestHandlerAsync(_eventBusQueueName, new RequestHandlerLister(
                async request =>
                {
                    object? responseObject = await HandleConnectorRequest(request);
                    return responseObject is not null ? Json.Serialize(responseObject) : null;
                },
                async () =>
                {
                    _logger.Error("Event bus request handler error");
                    BeginShutdown();
                }
            ));

            var @lock = await _subprocessLock.LockAsync(CancellationToken.None);

            if (!_shuttingDown)
            {
                await StartServerProcessAsync();

                if (_serverProcess is not null)
                {
                    await @lock.DisposeAsync();
                    await _serverProcess.WaitForExitAsync();
                    @lock = await _subprocessLock.LockAsync(CancellationToken.None);
                    var exitCode = _serverProcess.ExitCodeText;
                    _serverProcess.Dispose();
                    _serverProcess = null;
                    if (!_shuttingDown)
                    {
                        _logger.Warning($"Server process has unexpectedly terminated with exit code {exitCode}");
                    }
                    else
                    {
                        _logger.Information($"Server has finished with exit code {exitCode}");
                    }

                    _shuttingDown = true;

                    if (_bridgeProcess is not null)
                    {
                        _logger.Information("Bridge is still running, shutting it down now");
                        await @lock.DisposeAsync();
                        await _bridgeProcess.StopAndWaitAsync();
                        exitCode = _bridgeProcess.ExitCodeText;
                        @lock = await _subprocessLock.LockAsync(CancellationToken.None);
                        _bridgeProcess.Dispose();
                        _bridgeProcess = null;
                        _logger.Information($"Bridge has finished with exit code {exitCode}");
                    }
                }
                else
                {
                    _logger.Information("Server failed to start");
                }
            }

            await @lock.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Unhandled exception");
        }
        finally
        {
            if (_subscriber is not null)
            {
                await _subscriber.CloseAsync();
            }

            if (_requestHandler is not null)
            {
                await _requestHandler.CloseAsync();
            }

            if (_publisher is not null)
            {
                await _publisher.FlushAsync();
                await _publisher.CloseAsync();
            }

            if (_requestSender is not null)
            {
                await _requestSender.FlushAsync();
                await _requestSender.CloseAsync();
            }

            CleanupBaseDir();

            _serverProcess?.Dispose();
            _bridgeProcess?.Dispose();

            _logger.Information("Finished");
        }
    }

    private async Task HandleConnectorEvent(SubscriberEvent @event)
    {
        switch (@event.Type)
        {
            case "started":
                {
                    _logger.Information("Server is ready");
                    await StartBridgeProcessAsync();
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
                                _logger.Information("Saving snapshot");
                                SendEventBusRequest<object>("saved", worldSavedMessage, false)
                                    .Forget();
                            }
                            else
                            {
                                _logger.Information("Not saving snapshot because host player never connected");
                            }
                        }
                    }
                    else
                    {
                        _logger.Information("Ignoring save data because saving is disabled");
                    }
                }

                break;
            case "inventoryAdd":
                {
                    InventoryAddItemMessage? inventoryAddItemMessage = ReadJson<InventoryAddItemMessage>(@event.Data);
                    if (inventoryAddItemMessage is not null)
                    {
                        SendEventBusRequest<object>("inventoryAdd", inventoryAddItemMessage, false)
                            .Forget();
                    }
                }

                break;
            case "inventoryUpdateWear":
                {
                    InventoryUpdateItemWearMessage? inventoryUpdateItemWearMessage = ReadJson<InventoryUpdateItemWearMessage>(@event.Data);
                    if (inventoryUpdateItemWearMessage is not null)
                    {
                        SendEventBusRequest<object>("inventoryUpdateWear", inventoryUpdateItemWearMessage, false)
                            .Forget();
                    }
                }

                break;

            case "inventorySetHotbar":
                {
                    InventorySetHotbarMessage? inventorySetHotbarMessage = ReadJson<InventorySetHotbarMessage>(@event.Data);
                    if (inventorySetHotbarMessage is not null)
                    {
                        SendEventBusRequest<object>("inventorySetHotbar", inventorySetHotbarMessage, false)
                            .Forget();
                    }
                }

                break;
        }
    }

    private async Task<object?> HandleConnectorRequest(RequestHandlerRequest request)
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
                            _logger.Information($"Rejecting player connection for player {playerConnectedRequest.Uuid} because the host player must connect first");
                            return new PlayerConnectedResponse(false, null);
                        }

                        PlayerConnectedResponse? playerConnectedResponse = await SendEventBusRequest<PlayerConnectedResponse>("playerConnected", playerConnectedRequest, true);
                        if (playerConnectedResponse is not null)
                        {
                            _logger.Information($"Player {playerConnectedRequest.Uuid} has connected");

                            if (_playerId is not null && !_hostPlayerConnected && playerConnectedRequest.Uuid == _playerId)
                            {
                                _hostPlayerConnected = true;
                            }

                            return playerConnectedResponse;
                        }
                        else
                        {
                            Log.Debug("[playerConnected] invalid api response");
                        }
                    }
                    else
                    {
                        Log.Debug("[playerConnected] failed to read json");
                    }
                }

                break;
            case "playerDisconnected":
                {
                    PlayerDisconnectedRequest? playerDisconnectedRequest = ReadJson<PlayerDisconnectedRequest>(request.Data);
                    if (playerDisconnectedRequest is not null)
                    {
                        PlayerDisconnectedResponse? playerDisconnectedResponse = await SendEventBusRequest<PlayerDisconnectedResponse>("playerDisconnected", playerDisconnectedRequest, true);
                        if (playerDisconnectedResponse is not null)
                        {
                            _logger.Information($"Player {playerDisconnectedRequest.PlayerId} has disconnected");

                            if (_shutdownTime is null && _playerId is not null && playerDisconnectedRequest.PlayerId == _playerId)
                            {
                                _logger.Information("Host player has disconnected, beginning shutdown");
                                BeginShutdown();
                            }

                            return playerDisconnectedResponse;
                        }
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
                    }
                }

                break;
            case "getInventory":
                {
                    string? playerId = ReadJson<string>(request.Data);
                    if (playerId is not null)
                    {
                        InventoryResponse? inventoryResponse = await SendEventBusRequest<InventoryResponse>("getInventory", playerId, true);
                        if (inventoryResponse is not null)
                        {
                            return inventoryResponse;
                        }
                        else
                        {
                            Log.Debug("[getInventory] invalid api response");

                        }
                    }
                    else
                    {
                        Log.Debug("[getInventory] failed to read json");
                    }
                }

                break;
            case "inventoryRemove":
                {
                    InventoryRemoveItemRequest? inventoryRemoveItemRequest = ReadJson<InventoryRemoveItemRequest>(request.Data);
                    if (inventoryRemoveItemRequest is not null)
                    {
                        if (inventoryRemoveItemRequest.InstanceId is not null)
                        {
                            bool? success = await SendEventBusRequest<bool?>("inventoryRemove", inventoryRemoveItemRequest, true);
                            if (success is not null)
                            {
                                return success.Value;
                            }
                        }
                        else
                        {
                            int? removedCount = await SendEventBusRequest<int?>("inventoryRemove", inventoryRemoveItemRequest, true);
                            if (removedCount is not null)
                            {
                                return removedCount.Value;
                            }
                        }
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
                    else
                    {
                        Log.Debug("[findPlayer] failed to read json");
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
                    else
                    {
                        Log.Debug("[getInitialPlayerState] failed to read json");
                    }
                }

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

    private void SendEventBusInstanceStatusNotification(string status)
    {
        Debug.Assert(_publisher is not null);

        _publisher.PublishAsync("buildplates", status, InstanceId).ContinueWith(task =>
        {
            if (!task.Result)
            {
                Log.Error("Event bus publisher error");
                BeginShutdown();
            }
        });
    }

    private sealed record RequestWithInstanceId(
        string InstanceId,
        object Request
    );

    private Task<T?> SendEventBusRequest<T>(string type, object obj, bool returnResponse)
    {
        var request = new RequestWithInstanceId(InstanceId, obj);

        return SendEventBusRequestRaw<T>(type, request, returnResponse);
    }

    private async Task<T?> SendEventBusRequestRaw<T>(string type, object obj, bool returnResponse)
    {
        Debug.Assert(_requestSender is not null);

        try
        {
            string? response = await _requestSender.RequestAsync("buildplates", type, Json.Serialize(obj));

            if (response is null)
            {
                Log.Error("Event bus request failed (no response)");
                BeginShutdown();
                return default;
            }

            if (returnResponse)
            {
                Debug.Assert(typeof(T) != typeof(object));
                return Json.Deserialize<T>(response);
            }
            else
            {
                Debug.Assert(typeof(T) == typeof(object));
                return default;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Event bus request failed: {ex}");
            BeginShutdown();
            return default;
        }
    }

    private async Task<DirectoryInfo?> SetupServerFiles(byte[] serverData)
    {
        var workDir = new DirectoryInfo(Path.Combine(_baseDir.FullName, "server"));
        if (!workDir.TryCreate())
        {
            _logger.Error("Could not create server working directory");
            return null;
        }

        if (!CopyServerFile(new FileInfo(Path.Combine(_serverTemplateDir.FullName, _fabricJarName)), new FileInfo(Path.Combine(workDir.FullName, _fabricJarName)), false))
        {
            _logger.Error("Fabric JAR {} does not exist in server template directory", _fabricJarName);
            return null;
        }

        bool warnedMissingServerFiles = false;
        if (!CopyServerFile(new DirectoryInfo(Path.Combine(_serverTemplateDir.FullName, ".fabric", "server")), new DirectoryInfo(Path.Combine(workDir.FullName, ".fabric", "server")), true))
        {
            if (!warnedMissingServerFiles)
            {
                _logger.Warning("Server files were not pre-downloaded in server template directory, it is recommended to pre-download all server files to improve instance start-up time and reduce network data usage");
                warnedMissingServerFiles = true;
            }
        }

        if (!CopyServerFile(new DirectoryInfo(Path.Combine(_serverTemplateDir.FullName, "libraries")), new DirectoryInfo(Path.Combine(workDir.FullName, "libraries")), true))
        {
            if (!warnedMissingServerFiles)
            {
                _logger.Warning("Server files were not pre-downloaded in server template directory, it is recommended to pre-download all server files to improve instance start-up time and reduce network data usage");
                warnedMissingServerFiles = true;
            }
        }

        if (!CopyServerFile(new DirectoryInfo(Path.Combine(_serverTemplateDir.FullName, "versions")), new DirectoryInfo(Path.Combine(workDir.FullName, "versions")), true))
        {
            if (!warnedMissingServerFiles)
            {
                _logger.Warning("Server files were not pre-downloaded in server template directory, it is recommended to pre-download all server files to improve instance start-up time and reduce network data usage");
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                warnedMissingServerFiles = true;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
            }
        }

        if (!CopyServerFile(new DirectoryInfo(Path.Combine(_serverTemplateDir.FullName, "mods")), new DirectoryInfo(Path.Combine(workDir.FullName, "mods")), true))
        {
            _logger.Error("Mods directory was not present in server template directory, the buildplate server instance will not function correctly without the Fountain and Vienna Fabric mods installed");
        }

        await File.WriteAllTextAsync(Path.Combine(workDir.FullName, "eula.txt"), "eula=true");

        string serverProperties = new StringBuilder()
            .Append("online-mode=false\n")
            .Append("enforce-secure-profile=false\n")
            .Append("sync-chunk-writes=false\n")
            .Append("spawn-protection=0\n")
            .Append(CultureInfo.InvariantCulture, $"server-port={_serverInternalPort.ToString(CultureInfo.InvariantCulture)}\n")
            .Append(CultureInfo.InvariantCulture, $"gamemode={(_survival ? "survival" : "creative")}\n")
            .Append(CultureInfo.InvariantCulture, $"vienna-event-bus-address={_eventBusAddress}\n")
            .Append(CultureInfo.InvariantCulture, $"vienna-event-bus-queue-name={_eventBusQueueName}\n")
            .ToString();
        await File.WriteAllTextAsync(Path.Combine(workDir.FullName, "server.properties"), serverProperties);

        var worldDir = new DirectoryInfo(Path.Combine(workDir.FullName, "world"));
        if (!worldDir.TryCreate())
        {
            _logger.Error("Could not create server world directory");
            return null;
        }

        var worldEntitiesDir = new DirectoryInfo(Path.Combine(worldDir.FullName, "entities"));
        if (!worldEntitiesDir.TryCreate())
        {
            _logger.Error("Could not create server world entities directory");
            return null;
        }

        var worldRegionDir = new DirectoryInfo(Path.Combine(worldDir.FullName, "region"));
        if (!worldRegionDir.TryCreate())
        {
            _logger.Error("Could not create server world regions directory");
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

        using (var byteArrayInputStream = new MemoryStream(serverData))
        using (var zipInputStream = new ZipArchive(byteArrayInputStream))
        {
            foreach (ZipArchiveEntry entry in zipInputStream.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }

                string path = Path.Combine(worldDir.FullName, entry.FullName);

                using (Stream zipStream = entry.Open())
                using (FileStream fs = File.OpenWriteNew(path))
                {
                    zipStream.CopyTo(fs);
                }
            }
        }

        return workDir;
    }

    private static bool CopyServerFile(FileSystemInfo src, FileSystemInfo dst, bool directory)
    {
        if (!src.Exists)
        {
            return false;
        }

        if (directory)
        {
            ((DirectoryInfo)src).CopyTo(dst.FullName);
        }
        else
        {
            ((FileInfo)src).CopyTo(dst.FullName);
        }

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

#pragma warning disable IDE0060 // Remove unused parameter
    private DirectoryInfo? SetupBridgeFiles(byte[] serverData)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        var workDir = new DirectoryInfo(Path.Combine(_baseDir.FullName, "bridge"));
        if (!workDir.TryCreate())
        {
            _logger.Error("Could not create bridge working directory");
            return null;
        }

        // empty

        return workDir;
    }

    private void CleanupBaseDir()
    {
        _logger.Information("Cleaning up runtime directory");

        try
        {
            _baseDir.Delete(recursive: true);
        }
        catch (IOException exception)
        {
            _logger.Error(exception, "Exception while cleaning up runtime directory");
        }
    }

    private async Task StartServerProcessAsync()
    {
        await using (await _subprocessLock.LockAsync(CancellationToken.None))
        {
            if (_shuttingDown)
            {
                _logger.Debug("Already shutting down, not starting server process");
                return;
            }

            if (_serverProcess is not null)
            {
                _logger.Debug("Server process has already been started");
                return;
            }

            _logger.Information("Starting server process");

            try
            {
                bool useShellExecute = true;
                bool redirect = false;

                _serverProcess = new ConsoleProcess(_javaCmd, useShellExecute: useShellExecute, redirect: redirect, openInNewWindow: true);

                if (redirect && !useShellExecute)
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

                await _serverProcess.ExecuteAsync(_serverWorkDir.FullName, ["-jar", _fabricJarName, "-nogui"]);

                _logger.Information($"Server process started, PID {_serverProcess.Id}");
            }
            catch (IOException exception)
            {
                _logger.Error(exception, "Could not start server process");
            }
        }
    }

    private async Task StartBridgeProcessAsync()
    {
        await using (await _subprocessLock.LockAsync(CancellationToken.None))
        {
            if (_shuttingDown)
            {
                _logger.Debug("Already shutting down, not starting bridge process");
                return;
            }

            if (_bridgeProcess is not null)
            {
                _logger.Debug("Bridge process has already been started");
                return;
            }

            _logger.Information("Starting bridge process");

            try
            {
                bool useShellExecute = true;
                bool redirect = false;

                _bridgeProcess = new ConsoleProcess(_javaCmd, useShellExecute: useShellExecute, redirect: redirect, openInNewWindow: true);
                if (redirect && !useShellExecute)
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
                    Task.Run(async () =>
                    {
                        await using (await _subprocessLock.LockAsync(CancellationToken.None))
                        {
                            if (!_shuttingDown)
                            {
                                Log.Warning($"Bridge process has unexpectedly terminated with exit code {_bridgeProcess.ExitCode}");
                                _bridgeProcess.Dispose();
                                _bridgeProcess = null;
                                BeginShutdown();
                            }
                        }
                    }).Forget();
                };

                await _bridgeProcess.ExecuteAsync(_bridgeWorkDir!.FullName,
                [
                    "-jar", _fountainBridgeJar.FullName,
                    "-port", Port.ToString(CultureInfo.InvariantCulture),
                    "-serverAddress", "127.0.0.1",
                    "-serverPort", _serverInternalPort.ToString(CultureInfo.InvariantCulture),
                    "-connectorPluginJar", _connectorPluginJar.FullName,
                    "-connectorPluginClass", "micheal65536.vienna.buildplate.connector.plugin.ViennaConnectorPlugin",
                    "-connectorPluginArg", _connectorPluginArgString,
                    "-useUUIDAsUsername",
                ]);

                _logger.Information($"Bridge process started, PID {_bridgeProcess.Id}");
            }
            catch (IOException exception)
            {
                _logger.Error(exception, "Could not start bridge process");
            }
        }
    }

    private void StartHostPlayerConnectTimeout()
        => Task.Run(async () =>
        {
            await Task.Delay(checked((int)HOST_PLAYER_CONNECT_TIMEOUT));

            await using (await _subprocessLock.LockAsync(CancellationToken.None))
            {
                if (_shuttingDown)
                {
                    return;
                }
            }

            if (!_hostPlayerConnected)
            {
                _logger.Information("Host player has not connected yet, shutting down");
                BeginShutdown();
            }
        }).Forget();

    private void StartShutdownTimer()
        => Task.Run(async () =>
        {
            await Task.Yield();

            if (_shutdownTime is { } shutdownTime)
            {
                long currentTime = U.CurrentTimeMillis();
                while (currentTime < shutdownTime)
                {
                    long duration = shutdownTime - currentTime;
                    if (duration > 0)
                    {
                        _logger.Information("Server will shut down in {} milliseconds", duration);
                        await Task.Delay(checked((int)(duration > 2000 ? (duration / 2) : duration)));
                    }

                    currentTime = U.CurrentTimeMillis();
                }
            }

            _logger.Information("Shutdown time has been reached, shutting down");
            BeginShutdown();
        }).Forget();

    private void BeginShutdown()
        => Task.Run(async () =>
        {
            await Task.Yield();

            var @lock = await _subprocessLock.LockAsync(CancellationToken.None);

            if (_shuttingDown)
            {
                _logger.Debug("Already shutting down, not beginning shutdown");
                await @lock.DisposeAsync();
                return;
            }

            _shuttingDown = true;

            _logger.Information("Beginning shutdown");

            SendEventBusInstanceStatusNotification("shuttingDown");

            if (_bridgeProcess is not null)
            {
                _logger.Information("Waiting for bridge to shut down");
                await @lock.DisposeAsync();
                await _bridgeProcess.StopAndWaitAsync();
                var exitCode = _bridgeProcess.ExitCodeText;
                @lock = await _subprocessLock.LockAsync(CancellationToken.None);
                _bridgeProcess.Dispose();
                _bridgeProcess = null;
                _logger.Information($"Bridge has finished with exit code {exitCode}");
            }

            if (_serverProcess is not null)
            {
                _logger.Information("Asking the server to shut down");
                await _serverProcess.StopNoWaitAsync();
            }

            await @lock.DisposeAsync();
        }).Forget();

    public async Task WaitForShutdownAsync()
    {
        while (_thread is null)
        {
            await Task.Delay(50);
        }

        await _thread;
    }

    private sealed record BuildplateLoadRequest(
        string PlayerId,
        string BuildplateId
    );

    private sealed record SharedBuildplateLoadRequest(
        string SharedBuildplateId
    );

    private sealed record EncounterBuildplateLoadRequest(
        string EncounterBuildplateId
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