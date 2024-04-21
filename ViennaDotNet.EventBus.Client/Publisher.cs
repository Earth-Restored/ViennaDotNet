using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Client
{
    public sealed class Publisher
    {
        private readonly EventBusClient client;
        private readonly int channelId;

        private readonly object lockObj = new object();

        private bool _closed = false;

        private readonly LinkedList<string> queuedEvents = new();
        private readonly LinkedList<TaskCompletionSource<bool>> queuedEventResults = new();
        private TaskCompletionSource<bool>? currentPendingEventResult = null;

        internal Publisher(EventBusClient client, int channelId)
        {
            this.client = client;
            this.channelId = channelId;
        }

        public void close()
        {
            client.removePublisher(channelId);
            client.sendMessage(channelId, "CLOSE");
            closed();
        }

        public Task<bool> publish(string queueName, string type, string data)
        {
            if (!validateQueueName(queueName))
                throw new ArgumentException("Queue name contains invalid characters", nameof(queueName));

            if (!validateType(type))
                throw new ArgumentException("Type contains invalid characters", nameof(type));

            if (!validateData(data))
                throw new ArgumentException("Data contains invalid characters", nameof(data));

            string eventMessage = "SEND " + queueName + ":" + type + ":" + data;

            TaskCompletionSource<bool> completableFuture = new TaskCompletionSource<bool>();

            lock (lockObj)
            {
                if (_closed)
                    completableFuture.SetResult(false);
                else
                {
                    queuedEvents.AddLast(eventMessage);
                    queuedEventResults.AddLast(completableFuture);
                    if (currentPendingEventResult == null)
                        sendNextEvent();
                }
            }

            return completableFuture.Task;
        }

        internal bool handleMessage(string message)
        {
            if (message == "ACK")
            {
                lock (lockObj)
                {
                    if (currentPendingEventResult != null)
                    {
                        currentPendingEventResult.SetResult(true);
                        currentPendingEventResult = null;
                        if (!queuedEvents.IsEmpty())
                            sendNextEvent();

                        return true;
                    }
                    else
                        return false;
                }
            }
            else if (message == "ERR")
            {
                close();
                return true;
            }
            else
                return false;
        }

        private void sendNextEvent()
        {
            string message = queuedEvents.First!.Value;
            queuedEvents.RemoveFirst();
            client.sendMessage(channelId, message);
            currentPendingEventResult = queuedEventResults.First!.Value;
            queuedEventResults.RemoveFirst();
        }

        internal void closed()
        {
            lock (lockObj)
            {
                _closed = true;

                if (currentPendingEventResult != null)
                {
                    currentPendingEventResult.SetResult(false);
                    currentPendingEventResult = null;
                }
                queuedEventResults.ForEach(completableFuture=>completableFuture.SetResult(false));
                queuedEventResults.Clear();
                queuedEvents.Clear();
            }
        }

        private static bool validateQueueName(string queueName)
        {
            if (string.IsNullOrWhiteSpace(queueName) || queueName.Length == 0 || Regex.IsMatch(queueName, "[^A-Za-z0-9_\\-]") || Regex.IsMatch(queueName, "^[^A-Za-z0-9]"))
                return false;

            return true;
        }

        private static bool validateType(string type)
        {
            if (string.IsNullOrWhiteSpace(type) || type.Length == 0 || Regex.IsMatch(type, "[^A-Za-z0-9_\\-]") || Regex.IsMatch(type, "^[^A-Za-z0-9]"))
                return false;

            return true;
        }

        private static bool validateData(string str)
        {
            for (int i = 0; i < str.Length; i++)
                if (str[i] < 32 || str[i] >= 127)
                    return false;

            return true;
        }
    }
}
