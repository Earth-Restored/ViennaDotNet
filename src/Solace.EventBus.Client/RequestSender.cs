using System.Text.RegularExpressions;

namespace ViennaDotNet.EventBus.Client;

public sealed class RequestSender
{
    private readonly EventBusClient _client;
    private readonly int _channelId;
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    private bool _closed = false;
    private readonly Queue<string> _queuedRequests = new();
    private readonly Queue<TaskCompletionSource<string?>> _queuedRequestResponses = new();
    private TaskCompletionSource<string?>? _currentPendingResponse = null;

    internal RequestSender(EventBusClient client, int channelId)
    {
        _client = client;
        _channelId = channelId;
    }

    public async Task CloseAsync()
    {
        _client.RemoveRequestSender(_channelId);
        await _client.SendMessageAsync(_channelId, "CLOSE");
        await ClosedAsync();
    }

    public async Task<string?> RequestAsync(string queueName, string type, string data)
    {
        if (!ValidateQueueName(queueName)) throw new ArgumentException("Queue name contains invalid characters");
        if (!ValidateType(type)) throw new ArgumentException("Type contains invalid characters");
        if (!ValidateData(data)) throw new ArgumentException("Data contains invalid characters");

        string requestMessage = $"REQ {queueName}:{type}:{data}";
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await _lock.WaitAsync();
        try
        {
            if (_closed)
            {
                tcs.SetResult(null);
            }
            else
            {
                _queuedRequests.Enqueue(requestMessage);
                _queuedRequestResponses.Enqueue(tcs);

                if (_currentPendingResponse == null)
                {
                    await SendNextRequestAsync();
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        return await tcs.Task;
    }

    public async Task FlushAsync()
    {
        Task<string?>? taskToAwait = null;

        await _lock.WaitAsync();
        try
        {
            taskToAwait = _queuedRequestResponses.Count == 0 
                ? _currentPendingResponse?.Task 
                : _queuedRequestResponses.Last().Task; // Extension method or Peek for simple queue
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
        if (message == "ERR")
        {
            await CloseAsync();
            return true;
        }

        if (message == "ACK")
        {
            return true;
        }

        string? response;
        string[] parts = message.Split(' ', 2);
        
        if (parts[0] == "NREP")
        {
            if (parts.Length != 1) return false;
            response = null;
        }
        else if (parts[0] == "REP")
        {
            if (parts.Length != 2) return false;
            response = parts[1];
        }
        else
        {
            return false;
        }

        await _lock.WaitAsync();
        try
        {
            if (_currentPendingResponse != null)
            {
                _currentPendingResponse.SetResult(response);
                _currentPendingResponse = null;

                if (_queuedRequests.Count > 0)
                {
                    await SendNextRequestAsync();
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

    private async Task SendNextRequestAsync()
    {
        string message = _queuedRequests.Dequeue();
        await _client.SendMessageAsync(_channelId, message);
        _currentPendingResponse = _queuedRequestResponses.Dequeue();
    }

    internal async Task ClosedAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _closed = true;

            if (_currentPendingResponse != null)
            {
                _currentPendingResponse.TrySetResult(null);
                _currentPendingResponse = null;
            }

            while (_queuedRequestResponses.Count > 0)
            {
                _queuedRequestResponses.Dequeue().TrySetResult(null);
            }

            _queuedRequests.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool ValidateQueueName(string queueName)
    {
        return !string.IsNullOrEmpty(queueName) && 
               !queueName.Any(c => c < 32 || c >= 127) && 
               !Regex.IsMatch(queueName, "^[^A-Za-z0-9_\\-]$") && 
               !Regex.IsMatch(queueName, "^[^A-Za-z0-9]$");
    }

    private static bool ValidateType(string type)
    {
        return !string.IsNullOrEmpty(type) && 
               !type.Any(c => c < 32 || c >= 127) && 
               !Regex.IsMatch(type, "^[^A-Za-z0-9_\\-]$") && 
               !Regex.IsMatch(type, "^[^A-Za-z0-9]$");
    }

    private static bool ValidateData(string data)
    {
        return !data.Any(c => c < 32 || c >= 127);
    }
}