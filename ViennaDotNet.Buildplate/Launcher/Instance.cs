using Cyotek.Data.Nbt;
using Cyotek.Data.Nbt.Serialization;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Asn1;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.Buildplate.Connector.Model;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.Buildplate.Launcher
{
    public class Instance
    {
        public static Instance run(EarthDB earthDB, ObjectStoreClient objectStoreClient, EventBusClient eventBusClient, string playerId, string buildplateId, string instanceId, bool survival, bool night, string publicAddress, int port, int serverInternalPort, string javaCmd, FileInfo fountainBridgeJar, DirectoryInfo serverTemplateDir, string fabricJarName, FileInfo connectorPluginJar, DirectoryInfo baseDir, string eventBusConnectionstring, string apiServerAddress, string apiServerToken)
        {
            Instance instance = new Instance(earthDB, objectStoreClient, eventBusClient, playerId, buildplateId, instanceId, survival, night, publicAddress, port, serverInternalPort, javaCmd, fountainBridgeJar, serverTemplateDir, fabricJarName, connectorPluginJar, baseDir, eventBusConnectionstring, apiServerAddress, apiServerToken);
            new Thread(instance.run)
            {
                Name = $"Instance {instanceId}"
            }.Start();
            return instance;
        }

        private readonly EarthDB earthDB;
        private readonly ObjectStoreClient objectStoreClient;
        private readonly EventBusClient eventBusClient;

        private readonly string playerId;
        private readonly string buildplateId;
        public readonly string instanceId;
        private readonly bool survival;
        private readonly bool night;

        public readonly string publicAddress;
        public readonly int port;
        private readonly int serverInternalPort;

        private readonly string javaCmd;
        private readonly FileInfo fountainBridgeJar;
        private readonly DirectoryInfo serverTemplateDir;
        private readonly string fabricJarName;
        private readonly FileInfo connectorPluginJar;
        private readonly DirectoryInfo baseDir;
        private readonly string eventBusConnectionString;
        private readonly string apiServerAddress;
        private readonly string apiServerToken;

        private Thread thread;
        private readonly TaskCompletionSource readyFuture = new TaskCompletionSource();

        private readonly string eventBusQueueName;
        private Publisher? publisher = null;
        private Subscriber? subscriber = null;

        private readonly HttpClient okHttpClient;

        private DirectoryInfo? serverWorkDir;
        private DirectoryInfo? bridgeWorkDir;
        private Process? serverProcess = null;
        private Process? bridgeProcess = null;
        private bool shuttingDown = false;
        private readonly object subprocessLock = new object();

        private Instance(EarthDB earthDB, ObjectStoreClient objectStoreClient, EventBusClient eventBusClient, string playerId, string buildplateId, string instanceId, bool survival, bool night, string publicAddress, int port, int serverInternalPort, string javaCmd, FileInfo fountainBridgeJar, DirectoryInfo serverTemplateDir, string fabricJarName, FileInfo connectorPluginJar, DirectoryInfo baseDir, string eventBusConnectionstring, string apiServerAddress, string apiServerToken)
        {
            this.earthDB = earthDB;
            this.objectStoreClient = objectStoreClient;
            this.eventBusClient = eventBusClient;

            this.playerId = playerId;
            this.buildplateId = buildplateId;
            this.instanceId = instanceId;
            this.survival = survival;
            this.night = night;

            this.publicAddress = publicAddress;
            this.port = port;
            this.serverInternalPort = serverInternalPort;

            this.javaCmd = javaCmd;
            this.fountainBridgeJar = fountainBridgeJar;
            this.serverTemplateDir = serverTemplateDir;
            this.fabricJarName = fabricJarName;
            this.connectorPluginJar = connectorPluginJar;
            this.baseDir = baseDir;
            this.eventBusConnectionString = eventBusConnectionstring;
            this.apiServerAddress = apiServerAddress;
            this.apiServerToken = apiServerToken;

            this.eventBusQueueName = "buildplate_" + this.instanceId;

            okHttpClient = new HttpClient();
            //    okHttpClient = new OkHttpClient.Builder().addInterceptor(chain->

            //{
            //        return chain.proceed(chain.request().newBuilder().addHeader("Vienna-Buildplate-Instance-Token", apiServerToken).build());
            //    }).build();
        }

        private void run()
        {
            thread = Thread.CurrentThread;

            try
            {
                Log.Information("Starting for buildplate {buildplateId} player {playerId}");
                Log.Information("Using port {port} internal port {serverInternalPort}");

                Log.Information("Setting up server");

                Buildplates.Buildplate? buildplate;
                try
                {
                    Buildplates buildplates = (Buildplates)new EarthDB.Query(false)
                        .Get("buildplates", playerId, typeof(Buildplates))
                        .Execute(earthDB)
                        .Get("buildplates").Value;
                    buildplate = buildplates.getBuildplate(buildplateId);
                }
                catch (EarthDB.DatabaseException exception)
                {
                    Log.Error($"Could not load buildplate information: {exception}");
                    return;
                }
                if (buildplate is null)
                {
                    Log.Error($"Buildplate {buildplateId} does not exist for player {playerId}");
                    return;
                }
                byte[]? serverData = (byte[]?)objectStoreClient.get(buildplate.serverDataObjectId).Task.Result;
                if (serverData is null)
                {
                    Log.Error($"Server data object {buildplate.serverDataObjectId} for buildplate {buildplateId} could not be loaded from object store");
                    return;
                }

                try
                {

                    serverWorkDir = setupServerFiles(buildplate, serverData);
                    if (serverWorkDir == null)
                    {
                        Log.Error("Could not set up files for server");
                        return;
                    }
                }
                catch (IOException exception)
                {
                    Log.Error($"Could not set up files for server: {exception}");
                    return;
                }
                try
                {
                    bridgeWorkDir = setupBridgeFiles(buildplate, serverData);
                    if (bridgeWorkDir == null)
                    {
                        Log.Error("Could not set up files for bridge");
                        return;
                    }
                }
                catch (IOException exception)
                {
                    Log.Error("Could not set up files for bridge", exception);
                    return;
                }


                Log.Information("Running server");

                publisher = eventBusClient.addPublisher();
                subscriber = eventBusClient.addSubscriber(eventBusQueueName, new Subscriber.SubscriberListener(
                    @event => handleConnectorEvent(@event),
                    () =>
                    {
                        Log.Error("Event bus subscriber error");
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
                        int exitCode = waitForProcess(serverProcess);
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
                            bridgeProcess.Kill();//destroy();
                            Monitor.Exit(subprocessLock);
                            exitCode = waitForProcess(bridgeProcess);
                            Monitor.Enter(subprocessLock);
                            bridgeProcess = null;
                            Log.Information("Bridge has finished with exit code {}", exitCode);
                        }
                    }
                    else
                        Log.Information("Server failed to start");
                }
                Monitor.Exit(subprocessLock);
            }
            catch (Exception exception)
            {
                Log.Error($"Unhandled exception: {exception}");
            }
            finally
            {
                publisher?.close();

                subscriber?.close();

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
                        readyFuture.SetResult(/*null*/);
                    }
                    break;
                case "saved":
                    {
                        WorldSavedMessage? worldSavedMessage = readJson<WorldSavedMessage>(@event.data);
                        if (worldSavedMessage != null)
                        {
                            Log.Information("Saving snapshot");
                            sendApiServerRequest<object>($"/buildplate/snapshot/{playerId}/{buildplateId}", worldSavedMessage.dataBase64, false/*null*/);
                        }
                    }
                    break;
                case "playerConnectedRequest":
                    {
                        Request<PlayerConnectedRequest>? playerConnectedRequest = readRequestJson<PlayerConnectedRequest>(@event.data);
                        if (playerConnectedRequest != null)
                        {
                            if (playerConnectedRequest.request.uuid == playerId)    // TODO: probably remove this eventually and put in API server
                            {
                                PlayerConnectedResponse? playerConnectedResponse = sendApiServerRequest<PlayerConnectedResponse>($"/buildplate/join/{instanceId}", playerConnectedRequest.request, true);
                                if (playerConnectedResponse != null)
                                {
                                    sendConnectorResponse("playerConnectedResponse", playerConnectedRequest.id, playerConnectedResponse);
                                    Log.Information($"Player {playerConnectedRequest.request.uuid} has connected");
                                }
                            }
                            else
                                sendConnectorResponse("playerConnectedResponse", playerConnectedRequest.id, new PlayerConnectedResponse(false, null));
                        }
                    }
                    break;
                case "playerDisconnectedRequest":
                    {
                        Request<PlayerDisconnectedRequest>? playerDisconnectedRequest = readRequestJson<PlayerDisconnectedRequest>(@event.data);
                        if (playerDisconnectedRequest != null)
                        {
                            PlayerDisconnectedResponse? playerDisconnectedResponse = sendApiServerRequest<PlayerDisconnectedResponse>($"/buildplate/leave/{instanceId}/{playerDisconnectedRequest.request.playerId}", playerDisconnectedRequest.request, true);
                            if (playerDisconnectedResponse != null)
                            {
                                sendConnectorResponse("playerDisconnectedResponse", playerDisconnectedRequest.id, playerDisconnectedResponse);
                                Log.Information($"Player {playerDisconnectedRequest.request.playerId} has disconnected");

                                if (playerDisconnectedRequest.request.playerId == playerId)
                                {
                                    Log.Information("Host player has disconnected, beginning shutdown");
                                    beginShutdown();
                                }
                            }
                        }
                    }
                    break;
                case "inventoryAdd":
                    {
                        InventoryAddItemMessage? inventoryAddItemMessage = readJson<InventoryAddItemMessage>(@event.data);
                        if (inventoryAddItemMessage != null)
                            sendApiServerRequest<object>($"/buildplate/inventory/{instanceId}/{inventoryAddItemMessage.playerId}/add", inventoryAddItemMessage, false/*, null*/);
                    }
                    break;
                case "inventoryRemove":
                    {
                        InventoryRemoveItemMessage? inventoryRemoveItemMessage = readJson<InventoryRemoveItemMessage>(@event.data);
                        if (inventoryRemoveItemMessage != null)
                            sendApiServerRequest<object>($"/buildplate/inventory/{instanceId}/{inventoryRemoveItemMessage.playerId}/remove", inventoryRemoveItemMessage, false/*, null*/);
                    }
                    break;
                case "inventoryUpdateWear":
                    {
                        InventoryUpdateItemWearMessage? inventoryUpdateItemWearMessage = readJson<InventoryUpdateItemWearMessage>(@event.data);
                        if (inventoryUpdateItemWearMessage != null)
                            sendApiServerRequest<object>($"/buildplate/inventory/{instanceId}/{inventoryUpdateItemWearMessage.playerId}/updateWear", inventoryUpdateItemWearMessage, false/*, null*/);
                    }
                    break;
                case "inventorySetHotbar":
                    {
                        InventorySetHotbarMessage? inventorySetHotbarMessage = readJson<InventorySetHotbarMessage>(@event.data);
                        if (inventorySetHotbarMessage != null)
                            sendApiServerRequest<object>($"/buildplate/inventory/{instanceId}/{inventorySetHotbarMessage.playerId}/hotbar", inventorySetHotbarMessage, false/*, null*/);
                    }
                    break;
            }
        }

        private T? readJson<T>(string str)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(str);
            }
            catch (Exception exception)
            {
                Log.Error($"Failed to decode event bus message JSON: {exception}");
                beginShutdown();
                return default;
            }
        }

        private record Request<T>(
            string id,
            T request
        )
        {
        }

        private Request<T>? readRequestJson<T>(string str)
        {
            RequestResponseMessage? requestResponseMessage = readJson<RequestResponseMessage>(str);
            if (requestResponseMessage != null)
            {
                T? message = readJson<T>(requestResponseMessage.message);
                if (message != null)
                    return new Request<T>(requestResponseMessage.requestId, message);
            }
            return null;
        }

        private void sendConnectorResponse(string type, string requestId, object messageObject)
        {
            //Gson gson = new Gson().newBuilder().serializeNulls().create();
            Task<bool> completableFuture = publisher!.publish(eventBusQueueName, type, JsonConvert.SerializeObject(new RequestResponseMessage(requestId, JsonConvert.SerializeObject(messageObject))));
            completableFuture.ContinueWith(task =>
            {
                if (!task.Result)
                {
                    Log.Error("Event bus publisher error");
                    beginShutdown();
                }
            });
        }

        private T? sendApiServerRequest<T>(string path, object? obj, bool returnResponse/*, Type? responseClass*/)
        {
            return sendApiServerRequest<T>(path, JsonConvert.SerializeObject(obj), returnResponse/*, responseClass*/);
        }

        private T? sendApiServerRequest<T>(string path, string str, bool returnResponse/*, Type? responseClass*/)
        {
            // TODO: why + "/" +, when the path starts with / ??? remove?
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, apiServerAddress + "/" + path);
            request.Content = new StringContent(str, Encoding.UTF8);

            request.Headers.Add("Vienna-Buildplate-Instance-Token", apiServerToken);

            //RequestBody requestBody = RequestBody.create(string.getBytes(StandardCharsets.UTF_8));
            //Call call = okHttpClient.newCall(new okhttp3.Request.Builder().url("%s/%s".formatted(apiServerAddress, path)).post(requestBody).build());
            try
            {
                HttpResponseMessage response = okHttpClient.Send(request);
                HttpContent responseBody = response.Content;
                if (responseBody == null || response.StatusCode != HttpStatusCode.OK)
                {
                    Log.Error($"API server request failed {path} (code {response.StatusCode})");
                    beginShutdown();
                    return default;
                }
                if (returnResponse)
                    return JsonConvert.DeserializeObject<T>(responseBody.ReadAsStringAsync().Result);
                else
                    return default;
                //if (responseClass != null)
                //    return JsonConvert.DeserializeObject(responseBody.ReadAsStringAsync().Result, responseClass);
                //else
                //    return default;
            }

            catch (Exception exception)
            {
                Log.Error($"API server request failed {path}: {exception}");
                beginShutdown();
                return default;
            }
        }

        private DirectoryInfo? setupServerFiles(Buildplates.Buildplate buildplate, byte[] serverData)
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
            if (!copyServerFile(Path.Combine(serverTemplateDir.FullName, ".fabric/server"), Path.Combine(workDir.FullName, ".fabric/server"), true))
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
                Log.Error("Mods directory was not present in server template directory, the buildplate server instance will not function correctly without the Fountain Fabric mod installed");
            }

            File.WriteAllText(Path.Combine(workDir.FullName, "eula.txt"), "eula=true");

            string serverProperties = new StringBuilder()
                .Append("online-mode=false\n")
                .Append("enforce-secure-profile=false\n")
                .Append("spawn-protection=0\n")
                .Append($"server-port={serverInternalPort}\n")
                .Append($"fountain-connector-plugin-jar={connectorPluginJar.FullName}\n")
                .Append("fountain-connector-plugin-class=micheal65536.vienna.buildplate.connector.plugin.ViennaConnectorPlugin\n")
                .Append($"fountain-connector-plugin-arg={eventBusConnectionString}/{eventBusQueueName}\n")
                .Append($"gamemode={(survival ? "survival" : "creative")}\n")
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
            {
                TagWriter writer = new BinaryTagWriter(fs);
                writer.WriteStartDocument();
                writer.WriteTag(levelDatTag);
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
            //    try (ByteArrayInputStream byteArrayInputStream = new ByteArrayInputStream(serverData); ZipInputStream zipInputStream = new ZipInputStream(byteArrayInputStream))

            //{
            //        for (ZipEntry zipEntry = zipInputStream.getNextEntry(); zipEntry != null; zipEntry = zipInputStream.getNextEntry())
            //        {
            //            if (zipEntry.isDirectory())
            //            {
            //                zipInputStream.closeEntry();
            //                continue;
            //            }

            //            File file = new File(worldDir, zipEntry.getName());
            //            Files.copy(zipInputStream, file.toPath());
            //            zipInputStream.closeEntry();
            //        }
            //    }

            //    return workDir;
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
                        catch (ArgumentException exception)
                        {
                            throw new IOException(null, exception);
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
                        catch (ArgumentException exception)
                        {
                            throw new IOException(null, exception);
                        }
                        File.Copy(path, dstPath);
                        return FileVisitResult.CONTINUE;
                    },
                    (path, exception) =>
                    {
                        if (exception != null)
                            throw exception;

                        return FileVisitResult.CONTINUE;
                    },
                    (path, exception) =>
                    {
                        if (exception != null)
                            throw exception;

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

            TagCompound tag = new TagCompound("", dataTag.Value);
            return tag;
        }

        private DirectoryInfo? setupBridgeFiles(Buildplates.Buildplate buildplate, byte[] serverData)
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
                    (path, exception) =>
                    {
                        if (exception != null)
                            throw exception;

                        return FileVisitResult.CONTINUE;
                    },
                    (path, exception) =>
                    {
                        if (exception != null)
                            throw exception;

                        Directory.Delete(path);
                        return FileVisitResult.CONTINUE;
                    }
                ));
            }
            catch (IOException exception)
            {
                Log.Error($"Exception while cleaning up runtime directory: {exception}");
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
                ProcessStartInfo startInfo = new ProcessStartInfo(javaCmd, $"-jar ./{fabricJarName} -nogui")
                {
                    WorkingDirectory = serverWorkDir.FullName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                //serverProcess = new ProcessBuilder()
                //        .command(javaCmd, "-jar", "./" + fabricJarName, "-nogui")
                //        .directory(serverWorkDir)
                //        .redirectOutput(ProcessBuilder.Redirect.to(new File("log_%s-server".formatted(instanceId))))
                //        .redirectErrorStream(true)
                //        .start();
                serverProcess = new Process()
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true // needed for Process.Exited
                };

                StreamWriter? writer = new StreamWriter($"log_{instanceId}-server");
                serverProcess.OutputDataReceived += (sender, e) => writer?.WriteLine(e.Data);
                serverProcess.ErrorDataReceived += (sender, e) => writer?.WriteLine(e.Data);

                serverProcess.Exited += (sender, e) =>
                {
                    writer?.Close();
                    writer = null;
                };

                serverProcess.Start();

                serverProcess.BeginOutputReadLine();
                serverProcess.BeginErrorReadLine();

                Log.Information($"Server process started, PID {serverProcess.Id/*pid()*/}");
            }
            catch (IOException exception)
            {
                Log.Error($"Could not start server process: {exception}");
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
                //Process process = new ProcessBuilder()
                //        .command(javaCmd, "-jar", fountainBridgeJar.getAbsolutePath(),
                //                "-port", Integer.toString(port),
                //                "-serverAddress", "127.0.0.1",
                //                "-serverPort", Integer.toString(serverInternalPort),
                //                "-connectorPluginJar", connectorPluginJar.getAbsolutePath(),
                //                "-connectorPluginClass", "micheal65536.vienna.buildplate.connector.plugin.ViennaConnectorPlugin",
                //                "-connectorPluginArg", eventBusConnectionString + "/" + eventBusQueueName)
                //        .directory(bridgeWorkDir)
                //        .redirectOutput(ProcessBuilder.Redirect.to(new File("log_%s-bridge".formatted(instanceId))))
                //        .redirectErrorStream(true)
                //        .start();
                ProcessStartInfo startInfo = new ProcessStartInfo(javaCmd, new string[]
                {
                    "-jar", fountainBridgeJar.FullName,
                    "-port", port.ToString(),
                    "-serverAddress", "127.0.0.1",
                    "-serverPort", serverInternalPort.ToString(),
                    "-connectorPluginJar", connectorPluginJar.FullName,
                    "-connectorPluginClass", "micheal65536.vienna.buildplate.connector.plugin.ViennaConnectorPlugin",
                    "-connectorPluginArg", eventBusConnectionString + "/" + eventBusQueueName
                })
                {
                    WorkingDirectory = bridgeWorkDir.FullName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process process = new Process()
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                StreamWriter? writer = new StreamWriter($"log_{instanceId}-server");
                process.OutputDataReceived += (sender, e) => writer?.WriteLine(e.Data);
                process.ErrorDataReceived += (sender, e) => writer?.WriteLine(e.Data);

                process.Exited += (sender, e) =>
                {
                    writer?.Close();
                    writer = null;

                    Monitor.Enter(subprocessLock);
                    if (!shuttingDown)
                    {
                        Log.Warning($"Bridge process has unexpectedly terminated with exit code {process.ExitCode}");
                        bridgeProcess = null;
                        beginShutdown();
                    }
                    Monitor.Exit(subprocessLock);
                };

                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bridgeProcess = process;
                Log.Information($"Bridge process started, PID {bridgeProcess.Id/*.pid()*/}");

                //new Thread(() =>
                //{
                //    waitForProcess(process);
                //    Monitor.Enter(subprocessLock);
                //    if (!shuttingDown)
                //    {
                //        logger.warn("Bridge process has unexpectedly terminated with exit code {}", process.exitValue());
                //        bridgeProcess = null;
                //        beginShutdown();
                //    }
                //    Monitor.Exit(subprocessLock);
                //}).Start();
            }
            catch (IOException exception)
            {
                Log.Error($"Could not start bridge process: {exception}");
            }

            Monitor.Exit(subprocessLock);
        }

        private void beginShutdown()
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

            if (bridgeProcess != null)
            {
                Log.Information("Waiting for bridge to shut down");
                bridgeProcess.Kill();
                Monitor.Exit(subprocessLock);
                int exitCode = waitForProcess(bridgeProcess);
                Monitor.Enter(subprocessLock);
                bridgeProcess = null;
                Log.Information($"Bridge has finished with exit code {exitCode}");
            }

            if (serverProcess != null)
            {
                Log.Information("Asking the server to shut down");
                serverProcess.Kill();
            }

            Monitor.Exit(subprocessLock);
        }

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
                catch (ThreadAbortException)
                {
                    continue;
                }
            }
            return exitCode;
        }

        public void waitForReady()
        {
            for (; ; )
            {
                try
                {
                    if (!readyFuture.Task.Wait(1000))
                        throw new TimeoutException();
                    break;
                }
                catch (AggregateException ex) when (ex.InnerExceptions.Any(e => e is TaskCanceledException))
                {
                    continue;
                }
                catch (ThreadInterruptedException)
                {
                    continue;
                }
                catch (TimeoutException)
                {
                    if (!thread.IsAlive)
                        break;
                }
            }
        }

        public void waitForShutdown()
        {
            for (; ; )
            {
                try
                {
                    thread.Join();
                    break;
                }
                catch (ThreadAbortException)
                {
                    continue;
                }
            }
        }
    }
}
