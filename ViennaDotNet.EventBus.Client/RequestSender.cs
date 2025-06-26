using System.Text.RegularExpressions;

namespace ViennaDotNet.EventBus.Client;

public sealed partial class RequestSender
{
    private readonly EventBusClient _client;
    private readonly int _channelId;

    private readonly object _lock = new();

    private bool _closed = false;

    private readonly LinkedList<string> _queuedRequests = new();
    private readonly LinkedList<TaskCompletionSource<string?>> _queuedRequestResponses = new();
    private TaskCompletionSource<string?>? _currentPendingResponse = null;

    internal RequestSender(EventBusClient client, int channelId)
    {
        _client = client;
        _channelId = channelId;
    }

    public void Close()
    {
        _client.RemoveRequestSender(_channelId);
        _client.SendMessage(_channelId, "CLOSE");
        Closed();
    }

    public TaskCompletionSource<string?> Request(string queueName, string type, string data)
    {
        if (!ValidateQueueName(queueName))
            throw new ArgumentException("Queue name contains invalid characters");

        if (!ValidateType(type))
            throw new ArgumentException("Type contains invalid characters");

        if (!ValidateData(data))
            throw new ArgumentException("Data contains invalid characters");

        string requestMessage = "REQ " + queueName + ":" + type + ":" + data;

        TaskCompletionSource<string?> completableFuture = new();

        Monitor.Enter(_lock);
        if (_closed)
            completableFuture.SetResult(null);
        else
        {
            _queuedRequests.AddLast(requestMessage);
            _queuedRequestResponses.AddLast(completableFuture);
            if (_currentPendingResponse is null)
                SendNextRequest();
        }

        Monitor.Exit(_lock);

        return completableFuture;
    }

    public void Flush()
    {
        Monitor.Enter(_lock);
        var task = _queuedRequestResponses.Count == 0 ? _currentPendingResponse : _queuedRequestResponses.Last!.Value;
        Monitor.Exit(_lock);

        task?.Task.Wait();
    }

    internal Task<bool> HandleMessage(string message)
    {
        if (message == "ERR")
        {
            Close();
            return Task.FromResult(true);
        }
        else if (message == "ACK")
            return Task.FromResult(true);
        else
        {
            string? response;

            string[] parts = message.Split(' ', 2);
            if (parts[0] == "NREP")
            {
                if (parts.Length != 1)
                    return Task.FromResult(false);

                response = null;
            }
            else if (parts[0] == "REP")
            {
                if (parts.Length != 2)
                    return Task.FromResult(false);

                response = parts[1];
            }
            else
                return Task.FromResult(false);

            try
            {
                Monitor.Enter(_lock);
                if (_currentPendingResponse is not null)
                {
                    _currentPendingResponse.SetResult(response);
                    _currentPendingResponse = null;
                    if (_queuedRequests.Count != 0)
                        SendNextRequest();

                    return Task.FromResult(true);
                }
                else
                    return Task.FromResult(false);
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }
    }

    private void SendNextRequest()
    {
        string message = _queuedRequests.First!.Value;
        _queuedRequests.RemoveFirst();
        _client.SendMessage(_channelId, message);
        _currentPendingResponse = _queuedRequestResponses.First!.Value;
        _queuedRequestResponses.RemoveFirst();
    }

    internal void Closed()
    {
        Monitor.Enter(_lock);

        _closed = true;

        if (_currentPendingResponse is not null)
        {
            _currentPendingResponse.TrySetResult(null);
            _currentPendingResponse = null;
        }

        foreach (var completableFuture in _queuedRequestResponses)
        {
            completableFuture.TrySetResult(null);
        }

        _queuedRequestResponses.Clear();
        _queuedRequests.Clear();
        Monitor.Exit(_lock);
    }

    private static bool ValidateQueueName(string queueName)
        => !string.IsNullOrWhiteSpace(queueName) && queueName.Length != 0 && !GetRegex1().IsMatch(queueName) && !GetRegex2().IsMatch(queueName);

    private static bool ValidateType(string type)
        => !string.IsNullOrWhiteSpace(type) && type.Length != 0 && !GetRegex1().IsMatch(type) && !GetRegex2().IsMatch(type);

    private static bool ValidateData(string str)
    {
        for (int i = 0; i < str.Length; i++)
        {
            if (str[i] < 32 || str[i] >= 127)
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex("[^A-Za-z0-9_\\-]")]
    private static partial Regex GetRegex1();

    [GeneratedRegex("^[^A-Za-z0-9]")]
    private static partial Regex GetRegex2();
}
