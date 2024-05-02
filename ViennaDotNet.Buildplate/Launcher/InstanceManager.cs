using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Uma.Uuid;
using ViennaDotNet.Buildplate.Launcher;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.Buildplate.Launcher
{
    // TODO: need to deal with instances that are idle for too long with no player connecting
    public class InstanceManager
    {
        private readonly Starter starter;
        private readonly PreviewGenerator previewGenerator;

        private readonly Publisher publisher;
        private readonly RequestHandler requestHandler;
        private int runningInstanceCount = 0;
        private bool shuttingDown = false;
        private readonly object lockObj = new bool();

        record StartRequest(
            string instanceId,
            string playerId,
            string buildplateId,
            bool survival,
            bool night
        )
        {
        }

        record StartNotification(
            string instanceId,
            string playerId,
            string buildplateId,
            string address,
            int port
        )
        {
        }

        record PreviewRequest(
            string serverDataBase64,
            bool night
        )
        {
        }
        public InstanceManager(EventBusClient eventBusClient, Starter starter, PreviewGenerator previewGenerator)
        {
            this.starter = starter;
            this.previewGenerator = previewGenerator;

            publisher = eventBusClient.addPublisher();

            requestHandler = eventBusClient.addRequestHandler("buildplates", new RequestHandler.Handler(
                request =>
                {
                    if (request.type == "start")
                    {
                        Monitor.Enter(lockObj);
                        if (shuttingDown)
                        {
                            Monitor.Exit(lockObj);
                            return null;
                        }
                        runningInstanceCount += 1;
                        Monitor.Exit(lockObj);

                        StartRequest startRequest;
                        try

                        {
                            startRequest = JsonConvert.DeserializeObject<StartRequest>(request.data)!;
                        }
                        catch (Exception exception)
                        {
                            Log.Warning($"Bad start request: {exception}");
                            return null;
                        }

                        string instanceId = U.RandomUuid().ToString();
                        Log.Information($"Starting buildplate instance {instanceId} for player {startRequest.playerId} buildplate {startRequest.buildplateId}");

                        Instance? instance = starter.startInstance(instanceId, startRequest.playerId, startRequest.buildplateId, startRequest.survival, startRequest.night);
                        if (instance == null)
                        {
                            Log.Error($"Error starting buildplate instance {instanceId}");
                            return null;
                        }
                        sendEventBusMessageJson("started", new StartNotification(
                            instanceId,
                            startRequest.playerId,
                            startRequest.buildplateId,
                            instance.publicAddress,
                            instance.port
                        ));

                        new Thread(() =>
                        {
                            instance.waitForReady();

                            sendEventBusMessage("ready", instance.instanceId);

                            instance.waitForShutdown();

                            sendEventBusMessage("stopped", instance.instanceId);

                            Monitor.Enter(lockObj);
                            runningInstanceCount -= 1;
                            Monitor.Exit(lockObj);
                        }).Start();

                        return instanceId;
                    }
                    else if (request.type == "preview")
                    {
                        PreviewRequest previewRequest;
                        byte[] serverData;
                        try
                        {
                            previewRequest = JsonConvert.DeserializeObject<PreviewRequest>(request.data)!;
                            serverData = Convert.FromBase64String(previewRequest.serverDataBase64);
                        }
                        catch (Exception exception)
                        {
                            Log.Warning($"Bad preview request: {exception}");
                            return null;
                        }

                        Log.Information("Generating buildplate preview");

                        string? preview = previewGenerator.generatePreview(serverData, previewRequest.night);
                        if (preview == null)
                            Log.Warning("Could not generate preview for buildplate");

                        return preview;
                    }
                    else
                        return null;
                },
                () =>
                {
                    Log.Error("Event bus request handler error");
                }
            ));
        }

        private void sendEventBusMessage(string type, string message)
        {
            publisher.publish("buildplates", type, message).ContinueWith(task =>
            {
                if (!task.Result)
                    Log.Error("Event bus publisher error");
            });
        }

        private void sendEventBusMessageJson(string type, object messageObject)
        {
            sendEventBusMessage(type, JsonConvert.SerializeObject(messageObject));
        }

        public void shutdown()
        {
            requestHandler.close();
            publisher.close();

            Monitor.Enter(lockObj);
            shuttingDown = true;
            Log.Information($"Shutdown signal received, no new buildplate instances will be started, waiting for {runningInstanceCount} instances to finish");
            while (runningInstanceCount > 0)
            {
                int runningInstanceCount = this.runningInstanceCount;
                Monitor.Exit(lockObj);

                try
                {
                    Thread.Sleep(1000);
                }
                catch (ThreadAbortException)
                {
                    // empty
                }

                Monitor.Enter(lockObj);
                if (this.runningInstanceCount != runningInstanceCount)
                    Log.Information($"Waiting for {this.runningInstanceCount} instances to finish");
            }
            Monitor.Exit(lockObj);
        }
    }
}
