using System.Text.RegularExpressions;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Client;

public sealed partial class Publisher
{
    private readonly EventBusClient _client;
    private readonly int _channelId;

    private readonly object _lock = new();

    private bool _closed = false;

    // TODO: probably should be a queue
    private readonly LinkedList<string> _queuedEvents = new();
    private readonly LinkedList<TaskCompletionSource<bool>> _queuedEventResults = new();
    private TaskCompletionSource<bool>? _currentPendingEventResult = null;

    internal Publisher(EventBusClient client, int channelId)
    {
        _client = client;
        _channelId = channelId;
    }

    public void Close()
    {
        _client.RemovePublisher(_channelId);
        _client.SendMessage(_channelId, "CLOSE");
        Closed();
    }

    public Task<bool> Publish(string queueName, string type, string data)
    {
        if (!ValidateQueueName(queueName))
            throw new ArgumentException("Queue name contains invalid characters", nameof(queueName));

        if (!ValidateType(type))
            throw new ArgumentException("Type contains invalid characters", nameof(type));

        if (!ValidateData(data))
            throw new ArgumentException("Data contains invalid characters", nameof(data));

        string eventMessage = "SEND " + queueName + ":" + type + ":" + data;

        TaskCompletionSource<bool> completableFuture = new TaskCompletionSource<bool>();

        lock (_lock)
        {
            if (_closed)
                completableFuture.SetResult(false);
            else
            {
                _queuedEvents.AddLast(eventMessage);
                _queuedEventResults.AddLast(completableFuture);
                if (_currentPendingEventResult is null)
                {
                    SendNextEvent();
                }
            }
        }

        return completableFuture.Task;
    }

    public void Flush()
    {
        Monitor.Enter(_lock);
        var task = _queuedEventResults.Count == 0 ? _currentPendingEventResult : _queuedEventResults.Last!.Value;
        Monitor.Exit(_lock);

        task?.Task.Wait();
    }

    internal Task<bool> HandleMessage(string message)
    {
        if (message == "ACK")
        {
            lock (_lock)
            {
                if (_currentPendingEventResult is not null)
                {
                    _currentPendingEventResult.SetResult(true);
                    _currentPendingEventResult = null;
                    if (!_queuedEvents.IsEmpty())
                        SendNextEvent();

                    return Task.FromResult(true);
                }
                else
                    return Task.FromResult(false);
            }
        }
        else if (message == "ERR")
        {
            Close();
            return Task.FromResult(true);
        }
        else
            return Task.FromResult(false);
    }

    private void SendNextEvent()
    {
        string message = _queuedEvents.First!.Value;
        _queuedEvents.RemoveFirst();
        _client.SendMessage(_channelId, message);
        _currentPendingEventResult = _queuedEventResults.First!.Value;
        _queuedEventResults.RemoveFirst();
    }

    internal void Closed()
    {
        lock (_lock)
        {
            _closed = true;

            if (_currentPendingEventResult is not null)
            {
                _currentPendingEventResult.SetResult(false);
                _currentPendingEventResult = null;
            }

            foreach (var task in _queuedEventResults)
            {
                task.SetResult(false);
            }

            _queuedEventResults.Clear();
            _queuedEvents.Clear();
        }
    }

    private static bool ValidateQueueName(string queueName)
        => !string.IsNullOrWhiteSpace(queueName) && queueName.Length != 0 && !GetRegex1().IsMatch(queueName) && !GetRegex2().IsMatch(queueName);

    private static bool ValidateType(string type)
        => !string.IsNullOrWhiteSpace(type) && type.Length != 0 && !GetRegex1().IsMatch(type) && !GetRegex2().IsMatch(type);

    private static bool ValidateData(string str)
    {
        for (int i = 0; i < str.Length; i++)
            if (str[i] < 32 || str[i] >= 127)
                return false;

        return true;
    }

    [GeneratedRegex("[^A-Za-z0-9_\\-]")]
    private static partial Regex GetRegex1();

    [GeneratedRegex("^[^A-Za-z0-9]")]
    private static partial Regex GetRegex2();
}
