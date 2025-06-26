using Serilog;
using System.Diagnostics;
using System.Text.Json.Serialization;
using ViennaDotNet.Buildplate.Connector.Model;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.Buildplate.Launcher;

public class InstanceManager
{
    private readonly Starter _starter;

    private readonly Publisher _publisher;
    private readonly RequestHandler _requestHandler;
    private int _runningInstanceCount = 0;
    private bool _shuttingDown = false;
    private readonly object _lock = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    private enum InstanceType
    {
        BUILD,
        PLAY,
        SHARED_BUILD,
        SHARED_PLAY,
        ENCOUNTER,
    }

    private sealed record StartRequest(
        string? PlayerId,
        string? EncounterId,
        string BuildplateId,
        bool Night,
        InstanceType Type,
        long ShutdownTime
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

    private sealed record PreviewRequest(
        string ServerDataBase64,
        bool Night
    );

    public InstanceManager(EventBusClient eventBusClient, Starter starter)
    {
        _starter = starter;

        _publisher = eventBusClient.AddPublisher();

        _requestHandler = eventBusClient.AddRequestHandler("buildplates", new RequestHandler.Handler(
           async request =>
            {
                if (request.Type == "start")
                {
                    Monitor.Enter(_lock);
                    if (_shuttingDown)
                    {
                        Monitor.Exit(_lock);
                        return null;
                    }

                    _runningInstanceCount += 1;
                    Monitor.Exit(_lock);

                    StartRequest startRequest;
                    try
                    {
                        startRequest = Json.Deserialize<StartRequest>(request.Data)!;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Bad start request: {ex}");
                        return null;
                    }

                    var (survival, saveEnabled, inventoryType, buildplateSource, shutdownTime) = startRequest.Type switch
                    {
                        InstanceType.BUILD => (false, true, InventoryType.SYNCED, Instance.BuildplateSource.PLAYER, (long?)null),
                        InstanceType.PLAY => (true, false, InventoryType.DISCARD, Instance.BuildplateSource.PLAYER, null),
                        InstanceType.SHARED_BUILD => (false, false, InventoryType.DISCARD, Instance.BuildplateSource.SHARED, null),
                        InstanceType.SHARED_PLAY => (true, false, InventoryType.DISCARD, Instance.BuildplateSource.SHARED, null),
                        InstanceType.ENCOUNTER => (true, false, InventoryType.BACKPACK, Instance.BuildplateSource.ENCOUNTER, startRequest.ShutdownTime),
                        _ => throw new UnreachableException(),
                    };

                    if (buildplateSource is Instance.BuildplateSource.PLAYER && startRequest.PlayerId is null)
                    {
                        Log.Warning("Bad start request");
                        return null;
                    }

                    string instanceId = U.RandomUuid().ToString();

                    Log.Information($"Starting buildplate instance {instanceId}");

                    Instance? instance = starter.StartInstance(instanceId, startRequest.PlayerId, startRequest.BuildplateId, buildplateSource, survival, startRequest.Night, saveEnabled, inventoryType, shutdownTime);
                    if (instance is null)
                    {
                        Log.Error($"Error starting buildplate instance {instanceId}");
                        return null;
                    }

                    SendEventBusMessage("started", Json.Serialize(new StartNotification(
                        instanceId,
                        startRequest.PlayerId,
                        startRequest.EncounterId,
                        startRequest.BuildplateId,
                        instance.PublicAddress,
                        instance.Port,
                        startRequest.Type
                    )));

                    new Thread(() =>
                    {
                        instance.WaitForShutdown();

                        SendEventBusMessage("stopped", instance.InstanceId);

                        Monitor.Enter(_lock);
                        _runningInstanceCount -= 1;
                        Monitor.Exit(_lock);
                    }).Start();

                    return instanceId;
                }
                else if (request.Type == "preview")
                {
                    PreviewRequest previewRequest;
                    byte[] serverData;
                    try
                    {
                        previewRequest = Json.Deserialize<PreviewRequest>(request.Data)!;
                        serverData = Convert.FromBase64String(previewRequest.ServerDataBase64);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Bad preview request: {ex}");
                        return null;
                    }

                    Log.Information("Generating buildplate preview");

                    string? preview = PreviewGenerator.GeneratePreview(serverData, previewRequest.Night);
                    if (preview is null)
                    {
                        Log.Warning("Could not generate preview for buildplate");
                    }

                    return preview;
                }
                else
                {
                    return null;
                }
            },
            () =>
            {
                Log.Error("Event bus request handler error");
            }
        ));
    }

    private void SendEventBusMessage(string type, string message)
        => _publisher.Publish("buildplates", type, message).ContinueWith(task =>
        {
            if (!task.Result)
                Log.Error("Event bus publisher error");
        });

    public void Shutdown()
    {
        _requestHandler.Close();

        Monitor.Enter(_lock);
        _shuttingDown = true;
        Log.Information($"Shutdown signal received, no new buildplate instances will be started, waiting for {_runningInstanceCount} instances to finish");
        while (_runningInstanceCount > 0)
        {
            int runningInstanceCount = _runningInstanceCount;
            Monitor.Exit(_lock);

            try
            {
                Thread.Sleep(1000);
            }
            catch (ThreadInterruptedException)
            {
                // empty
            }

            Monitor.Enter(_lock);
            if (_runningInstanceCount != runningInstanceCount)
            {
                Log.Information($"Waiting for {_runningInstanceCount} instances to finish");
            }
        }

        Monitor.Exit(_lock);

        _publisher.Flush();
        _publisher.Close();
    }
}
