using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Client
{
    public sealed class RequestSender
    {
        private readonly EventBusClient client;
        private readonly int channelId;

        private readonly object lockObj = new object();

        private bool _closed = false;

        private readonly LinkedList<string> queuedRequests = new();
        private readonly LinkedList<TaskCompletionSource<string?>> queuedRequestResponses = new();
        private TaskCompletionSource<string?>? currentPendingResponse = null;

        internal RequestSender(EventBusClient client, int channelId)
        {
            this.client = client;
            this.channelId = channelId;
        }

        public void close()
        {
            client.removeRequestSender(channelId);
            client.sendMessage(channelId, "CLOSE");
            closed();
        }

        public TaskCompletionSource<string?> request(string queueName, string type, string data)
        {
            if (!validateQueueName(queueName))
                throw new ArgumentException("Queue name contains invalid characters");

            if (!validateType(type))
                throw new ArgumentException("Type contains invalid characters");

            if (!validateData(data))
                throw new ArgumentException("Data contains invalid characters");

            string requestMessage = "REQ " + queueName + ":" + type + ":" + data;

            TaskCompletionSource<string?> completableFuture = new();

            Monitor.Enter(lockObj);
            if (_closed)
                completableFuture.SetResult(null);
            else
            {
                queuedRequests.AddLast(requestMessage);
                queuedRequestResponses.AddLast(completableFuture);
                if (currentPendingResponse == null)
                    sendNextRequest();
            }
            Monitor.Exit(lockObj);

            return completableFuture;
        }

        internal bool handleMessage(string message)
        {
            if (message == "ERR")
            {
                close();
                return true;
            }
            else if (message == "ACK")
                return true;
            else
            {
                string? response;

                string[] parts = message.Split(' ', 2);
                if (parts[0] == "NREP")
                {
                    if (parts.Length != 1)
                        return false;

                    response = null;
                }
                else if (parts[0] == "REP")
                {
                    if (parts.Length != 2)
                        return false;

                    response = parts[1];
                }
                else
                    return false;

                try
                {
                    Monitor.Enter(lockObj);
                    if (currentPendingResponse != null)
                    {
                        currentPendingResponse.SetResult(response);
                        currentPendingResponse = null;
                        if (queuedRequests.Count != 0)
                            sendNextRequest();

                        return true;
                    }
                    else
                        return false;
                }
                finally
                {
                    Monitor.Exit(lockObj);
                }
            }
        }

        private void sendNextRequest()
        {
            string message = queuedRequests.First!.Value;
            queuedRequests.RemoveFirst();
            client.sendMessage(channelId, message);
            currentPendingResponse = queuedRequestResponses.First!.Value;
            queuedRequestResponses.RemoveFirst();
        }

        internal void closed()
        {
            Monitor.Enter(lockObj);

            _closed = true;

            if (currentPendingResponse != null)
            {
                currentPendingResponse.TrySetResult(null);
                currentPendingResponse = null;
            }
            queuedRequestResponses.ForEach(completableFuture => completableFuture.TrySetResult(null));
            queuedRequestResponses.Clear();
            queuedRequests.Clear();
            Monitor.Exit(lockObj);
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
