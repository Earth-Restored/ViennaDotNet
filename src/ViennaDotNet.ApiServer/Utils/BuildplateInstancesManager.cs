using Serilog;
using System.Text.Json.Serialization;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.ApiServer.Utils;

public sealed class BuildplateInstancesManager
{
    private readonly EventBusClient _eventBusClient;
    private readonly Subscriber _subscriber;
    private readonly RequestSender _requestSender;

    private readonly Dictionary<string, TaskCompletionSource<bool>?> _pendingInstances = [];
    private readonly Dictionary<string, InstanceInfo> _instances = [];
    private readonly Dictionary<string, HashSet<string>> _instancesByBuildplateId = [];

    public BuildplateInstancesManager(EventBusClient eventBusClient)
    {
        _eventBusClient = eventBusClient;
        _subscriber = _eventBusClient.AddSubscriber("buildplates", new Subscriber.SubscriberListener(
            HandleEvent,
            () =>
            {
                Log.Fatal("Buildplates event bus subscriber error");
                Log.CloseAndFlush();
                Environment.Exit(1);
            }
        ));
        _requestSender = _eventBusClient.AddRequestSender();
    }

    public async Task<string?> RequestBuildplateInstance(string? playerId, string? encounterId, string buildplateId, InstanceType type, long shutdownTime, bool night)
    {
        if (playerId is null && type is not InstanceType.ENCOUNTER)
        {
            throw new ArgumentException();
        }

        if (encounterId is not null && type is not InstanceType.ENCOUNTER)
        {
            throw new ArgumentException();
        }

        if (playerId is not null && encounterId is not null)
        {
            Log.Information($"Finding buildplate instance for buildplate {buildplateId} type {type} encounter {encounterId} player {playerId}");
        }
        else if (playerId is not null)
        {
            Log.Information($"Finding buildplate instance for buildplate {buildplateId} type {type} player {playerId}");
        }
        else if (encounterId is not null)
        {
            Log.Information($"Finding buildplate instance for buildplate {buildplateId} type {type} encounter {encounterId}");
        }
        else
        {
            Log.Information($"Finding buildplate instance for buildplate {buildplateId} type {type}");
        }

        lock (_instances)
        {
            HashSet<string>? instanceIds = _instancesByBuildplateId.GetOrDefault(buildplateId);
            if (instanceIds is not null)
            {
                foreach (string loopInstanceId in instanceIds)
                {
                    InstanceInfo? instanceInfo = _instances.GetOrDefault(loopInstanceId);
                    if (instanceInfo is not null && !instanceInfo.ShuttingDown)
                    {
                        if (instanceInfo.Type == type &&
                            instanceInfo.PlayerId == playerId &&
                            instanceInfo.EncounterId == encounterId
                        )
                        {
                            Log.Information($"Found existing buildplate instance {loopInstanceId}");
                            return loopInstanceId;
                        }
                    }
                }
            }
        }

        Log.Information("Did not find existing instance, starting new instance");
        string? instanceId = await _requestSender.Request("buildplates", "start", Json.Serialize(new StartRequest(playerId, encounterId, buildplateId, night, type, shutdownTime))).Task;
        if (instanceId is null)
        {
            Log.Error("Buildplate start request was rejected/ignored");
            return null;
        }

        TaskCompletionSource<bool> completableFuture = new();
        lock (_instances)
        {
            if (_instances.ContainsKey(instanceId))
            {
                completableFuture.SetResult(true);
            }
            else
            {
                lock (_pendingInstances)
                {
                    _pendingInstances[instanceId] = completableFuture;
                }
            }
        }

        if (!await completableFuture.Task)
        {
            Log.Warning($"Could not start buildplate instance {instanceId}");
            return null;
        }

        return instanceId;
    }

    public InstanceInfo? GetInstanceInfo(string instanceId)
    {
        lock (_instances)
        {
            return _instances.GetOrDefault(instanceId, null);
        }
    }

    public string? GetBuildplatePreview(byte[] serverData, bool night)
    {
        Log.Information("Requesting buildplate preview");

        string? preview = _requestSender.Request("buildplates", "preview", Json.Serialize(new PreviewRequest(Convert.ToBase64String(serverData), night))).Task.Result;
        if (preview is null)
            Log.Error("Preview request was rejected/ignored");

        return preview;
    }

    private Task HandleEvent(Subscriber.Event @event)
    {
        switch (@event.Type)
        {
            case "started":
                {
                    StartNotification startNotification;
                    try
                    {
                        startNotification = Json.Deserialize<StartNotification>(@event.Data)!;
                        if (startNotification.PlayerId is null && startNotification.Type is not InstanceType.ENCOUNTER)
                        {
                            Log.Warning("Bad start notification");
                            return Task.CompletedTask;
                        }

                        lock (_instances)
                        {
                            Log.Information($"Buildplate instance {startNotification.InstanceId} has started");
                            _instances[startNotification.InstanceId] = new InstanceInfo(
                                startNotification.Type,
                                startNotification.InstanceId,
                                startNotification.PlayerId,
                                startNotification.EncounterId,
                                startNotification.BuildplateId,
                                startNotification.Address,
                                startNotification.Port,
                                false,
                                false
                            );

                            _instancesByBuildplateId.ComputeIfAbsent(startNotification.BuildplateId, buildplateId => [])!.Add(startNotification.InstanceId);
                        }

                        lock (_pendingInstances)
                        {
                            TaskCompletionSource<bool>? completableFuture = _pendingInstances.JavaRemove(startNotification.InstanceId);
                            completableFuture?.SetResult(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Bad start notification: {ex}");
                    }
                }

                break;
            case "ready":
                {
                    string instanceId = @event.Data;
                    lock (_instances)
                    {
                        InstanceInfo? instanceInfo = _instances.GetOrDefault(instanceId, null);
                        if (instanceInfo is not null)
                        {
                            Log.Information($"Buildplate instance {instanceId} is ready");
                            _instances[instanceId] = new InstanceInfo(
                                instanceInfo.Type,
                                instanceInfo.InstanceId,
                                instanceInfo.PlayerId,
                                instanceInfo.EncounterId,
                                instanceInfo.BuildplateId,
                                instanceInfo.Address,
                                instanceInfo.Port,
                                true,
                                instanceInfo.ShuttingDown
                            );
                        }
                    }
                }

                break;
            case "shuttingDown":
                {
                    string instanceId = @event.Data;
                    lock (_instances)
                    {
                        InstanceInfo? instanceInfo = _instances.GetValueOrDefault(instanceId);
                        if (instanceInfo is not null)
                        {
                            Log.Information($"Buildplate instance {instanceId} is shutting down");
                            _instances[instanceId] = new InstanceInfo(
                                instanceInfo.Type,
                                instanceInfo.InstanceId,
                                instanceInfo.PlayerId,
                                instanceInfo.EncounterId,
                                instanceInfo.BuildplateId,
                                instanceInfo.Address,
                                instanceInfo.Port,
                                instanceInfo.Ready,
                                true
                            );
                        }
                    }
                }

                break;
            case "stopped":
                {
                    string instanceId = @event.Data;
                    lock (_instances)
                    {
                        var instanceInfo = _instances.JavaRemove(instanceId);
                        if (instanceInfo is not null)
                        {
                            Log.Information($"Buildplate instance {instanceId} has stopped");

                            var instanceIds = _instancesByBuildplateId.GetOrDefault(instanceInfo.BuildplateId);
                            instanceIds?.Remove(instanceInfo.InstanceId);
                        }
                    }
                }

                break;
            default:
                Log.Warning($"Unknown event in BuildplateInstancesManager: '{@event.Type}'");
                break;
        }

        return Task.CompletedTask;
    }

    private sealed record StartRequest(
        string? PlayerId,
        string? EncounterId,
        string BuildplateId,
        bool Night,
        InstanceType Type,
        long ShutdownTime
    );

    private sealed record PreviewRequest(
        string ServerDataBase64,
        bool Night
    );

    private sealed record StartNotification(
        string InstanceId,
        string? PlayerId,
        string? EncounterId,
        string BuildplateId,
        string Address,
        int Port,
        InstanceType Type
    );

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InstanceType
    {
        BUILD,
        PLAY,
        SHARED_BUILD,
        SHARED_PLAY,
        ENCOUNTER,
    }

    public sealed record InstanceInfo(
        InstanceType Type,

        string InstanceId,

        string? PlayerId,
        string? EncounterId,
        string BuildplateId,

        string Address,
        int Port,

        bool Ready,
        bool ShuttingDown
    );
}
