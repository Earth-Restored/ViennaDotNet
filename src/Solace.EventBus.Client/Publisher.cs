using System.Text.RegularExpressions;
using Solace.Common.Utils;

namespace Solace.EventBus.Client;

public sealed class Publisher : IAsyncDisposable
{
    private readonly EventBusClient _client;
    private readonly int _channelId;
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    private bool _closed;
    private readonly LinkedList<string> _queuedEvents = new();
    private readonly LinkedList<TaskCompletionSource<bool>> _queuedEventResults = new();
    private TaskCompletionSource<bool>? _currentPendingEventResult;

    internal Publisher(EventBusClient client, int channelId)
    {
        _client = client;
        _channelId = channelId;
    }

    public async Task CloseAsync()
    {
        _client.RemovePublisher(_channelId);
        await _client.SendMessageAsync(_channelId, "CLOSE");
        await ClosedAsync();
    }

    public async Task<bool> PublishAsync(string queueName, string type, string data)
    {
        if (!ValidateQueueName(queueName))
        {
            throw new ArgumentException("Queue name contains invalid characters");
        }

        if (!ValidateType(type))
        {
            throw new ArgumentException("Type contains invalid characters");
        }

        if (!ValidateData(data))
        {
            throw new ArgumentException("Data contains invalid characters");
        }

        string eventMessage = $"SEND {queueName}:{type}:{data}";
        
        // Use RunContinuationsAsynchronously to prevent deadlocks when the result is set
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await _lock.WaitAsync();
        try
        {
            if (_closed)
            {
                tcs.SetResult(false);
            }
            else
            {
                _queuedEvents.AddLast(eventMessage);
                _queuedEventResults.AddLast(tcs);

                if (_currentPendingEventResult == null)
                {
                    await SendNextEventAsync();
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        return await tcs.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync();
        await CloseAsync();
    }

    public async Task FlushAsync()
    {
        Task<bool>? taskToAwait = null;

        await _lock.WaitAsync();
        try
        {
            taskToAwait = _queuedEventResults.Count == 0 
                ? _currentPendingEventResult?.Task 
                : _queuedEventResults.Last!.Value.Task;
        }
        finally
        {
            _lock.Release();
        }

        if (taskToAwait != null)
        {
            await taskToAwait;
        }
    }

    internal async Task<bool> HandleMessageAsync(string message)
    {
        if (message == "ACK")
        {
            await _lock.WaitAsync();
            try
            {
                if (_currentPendingEventResult != null)
                {
                    _currentPendingEventResult.SetResult(true);
                    _currentPendingEventResult = null;

                    if (_queuedEvents.Count > 0)
                    {
                        await SendNextEventAsync();
                    }

                    return true;
                }

                return false;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        if (message == "ERR")
        {
            await CloseAsync();
            return true;
        }
        
        return false;
    }

    private async Task SendNextEventAsync()
    {
        string message = _queuedEvents.First!.Value;
        _queuedEvents.RemoveFirst();

        await _client.SendMessageAsync(_channelId, message);

        _currentPendingEventResult = _queuedEventResults.First!.Value;
        _queuedEventResults.RemoveFirst();
    }

    internal async Task ClosedAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _closed = true;
            _currentPendingEventResult?.TrySetResult(false);
            _currentPendingEventResult = null;

            foreach (var tcs in _queuedEventResults)
            {
                tcs.TrySetResult(false);
            }

            _queuedEventResults.Clear();
            _queuedEvents.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool ValidateQueueName(string queueName)
        => !queueName.Any(c => c < 32 || c >= 127) &&
            !string.IsNullOrEmpty(queueName) &&
            !Regex.IsMatch(queueName, "^[^A-Za-z0-9_\\-]$") &&
            !Regex.IsMatch(queueName, "^^[^A-Za-z0-9]$");

    private static bool ValidateType(string type)
        => !type.Any(c => c < 32 || c >= 127) &&
            !string.IsNullOrEmpty(type) &&
            !Regex.IsMatch(type, "^[^A-Za-z0-9_\\-]$") &&
            !Regex.IsMatch(type, "^^[^A-Za-z0-9]$");

    private static bool ValidateData(string data)
        => !data.Any(c => c < 32 || c >= 127);
}