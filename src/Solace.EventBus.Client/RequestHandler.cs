using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Client;

public sealed class RequestHandlerLister : IRequestHandlerLister
{
    public Func<RequestHandlerRequest, Task<string?>>? OnRequest;
    public Func<Task>? OnError;

    public RequestHandlerLister(Func<RequestHandlerRequest, Task<string?>>? onRequest, Func<Task>? onError)
    {
        OnRequest = onRequest;
        OnError = onError;
    }

    public Task<string?> OnRequestAsync(RequestHandlerRequest request)
        => OnRequest?.Invoke(request) ?? Task.FromResult<string?>(null);

    public Task OnErrorAsync()
        => OnError?.Invoke() ?? Task.CompletedTask;
}

public interface IRequestHandlerLister
{
    Task<string?> OnRequestAsync(RequestHandlerRequest request);

    Task OnErrorAsync();
}

public sealed class RequestHandlerRequest
{
    public long Timestamp { get; }
    public string Type { get; }
    public string Data { get; }

    internal RequestHandlerRequest(long timestamp, string type, string data)
    {
        Timestamp = timestamp;
        Type = type;
        Data = data;
    }
}

public sealed class RequestHandler
{
    private readonly EventBusClient _client;
    private readonly int _channelId;
    private readonly string _queueName;
    private readonly IRequestHandlerLister _handler;
    private volatile bool _closed = false;

    internal RequestHandler(EventBusClient client, int channelId, string queueName, IRequestHandlerLister handler)
    {
        _client = client;
        _channelId = channelId;
        _queueName = queueName;
        _handler = handler;
    }

    public async Task CloseAsync()
    {
        _closed = true;
        _client.RemoveRequestHandler(_channelId);
        await _client.SendMessageAsync(_channelId, "CLOSE");
    }

    internal async Task<bool> HandleMessageAsync(string message)
    {
        if (message == "ERR")
        {
            await ErrorAsync();
            await _handler.OnErrorAsync();
            return true;
        }

        string[] fields = message.Split(':', 4);
        if (fields.Length != 4)
        {
            return false;
        }

        if (!int.TryParse(fields[0], out int requestId) || requestId <= 0)
        {
            return false;
        }

        if (!long.TryParse(fields[1], out long timestamp) || timestamp < 0)
        {
            return false;
        }

        string type = fields[2];
        string data = fields[3];

        _ = ProcessRequestAsync(requestId, timestamp, type, data);

        return true;
    }

    private async Task ProcessRequestAsync(int requestId, long timestamp, string type, string data)
    {
        try
        {
            string? response = await _handler.OnRequestAsync(new RequestHandlerRequest(timestamp, type, data));

            if (!_closed)
            {
                if (response != null)
                {
                    await _client.SendMessageAsync(_channelId, $"REP {requestId}:{response}");
                }
                else
                {
                    await _client.SendMessageAsync(_channelId, $"NREP {requestId}");
                }
            }
        }
        catch
        {
            if (!_closed)
            {
                await _client.SendMessageAsync(_channelId, $"NREP {requestId}");
            }
        }
    }

    internal async Task ErrorAsync()
    {
        _closed = true;
        await _handler.OnErrorAsync();
    }
}
