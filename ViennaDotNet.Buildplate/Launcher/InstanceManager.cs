using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.EventBus.Client;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace ViennaDotNet.Buildplate.Launcher
{
    // TODO: need to deal with instances that are idle for too long with no player connecting
    public class InstanceManager
    {
        private readonly Starter starter;

        private readonly Publisher publisher;
        private readonly Subscriber subscriber;
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
            StartNotification.Info? info
        )
        {
            public record Info(
                string playerId,
                string buildplateId,
                string address,
                int port
            )
            {
            }
        }

        public InstanceManager(EventBusClient eventBusClient, Starter starter)
        {
            this.starter = starter;

            publisher = eventBusClient.addPublisher();

            subscriber = eventBusClient.addSubscriber("buildplates", new Subscriber.SubscriberListener(
                @event =>
                {
                    if (@event.type == "startRequest")
                    {
                        Monitor.Enter(lockObj);
                        if (shuttingDown)
                        {
                            Monitor.Exit(lockObj);
                            return;
                        }
                        runningInstanceCount += 1;
                        Monitor.Exit(lockObj);

                        StartRequest startRequest;
                        try

                        {
                            startRequest = JsonConvert.DeserializeObject<StartRequest>(@event.data)!;
                        }
                        catch (Exception exception)
                        {
                            Log.Warning($"Bad start request: {exception}");
                            return;
                        }

                        Log.Information($"Starting buildplate instance {startRequest.instanceId} for player {startRequest.playerId} buildplate {startRequest.buildplateId}");

                        Instance? instance = starter.startInstance(startRequest.instanceId, startRequest.playerId, startRequest.buildplateId, startRequest.survival, startRequest.night);
                        if (instance == null)
                        {
                            Log.Error($"Error starting buildplate instance {startRequest.instanceId}");
                            sendEventBusMessageJson("started", new StartNotification(startRequest.instanceId, null));
                            return;
                        }
                        sendEventBusMessageJson("started", new StartNotification(startRequest.instanceId, new StartNotification.Info(
                            startRequest.playerId,
                            startRequest.buildplateId,
                            instance.publicAddress,
                            instance.port
                        )));

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
                    }
                },
                () =>
                {
                    Log.Error("Event bus subscriber error");
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
            subscriber.close();

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
