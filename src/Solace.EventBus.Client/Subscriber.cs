namespace Solace.EventBus.Client;

public sealed class SubscriberListener : ISubscriberListener
{
    public Func<SubscriberEvent, Task>? OnEvent;
    public Func<Task>? OnError;

    public SubscriberListener()
    {
    }
    public SubscriberListener(Func<SubscriberEvent, Task>? onEvent = null, Func<Task>? onError = null)
    {
        OnEvent = onEvent;
        OnError = onError;
    }

    public Task OnEventAsync(SubscriberEvent @event)
        => OnEvent?.Invoke(@event) ?? Task.CompletedTask;

    public Task OnErrorAsync()
        => OnError?.Invoke() ?? Task.CompletedTask;
}

public interface ISubscriberListener
{
#pragma warning disable CA1716 // Identifiers should not match keywords
    Task OnEventAsync(SubscriberEvent @event);
#pragma warning restore CA1716 // Identifiers should not match keywords

    Task OnErrorAsync();
}

public sealed class SubscriberEvent
{
    public long Timestamp { get; }
    public string Type { get; }
    public string Data { get; }

    internal SubscriberEvent(long timestamp, string type, string data)
    {
        Timestamp = timestamp;
        Type = type;
        Data = data;
    }
}

public sealed class Subscriber
{
    private readonly EventBusClient _client;
    private readonly ISubscriberListener _listener;

    internal int ChannelId { get; }
    internal string QueueName { get; }

    internal Subscriber(EventBusClient client, int channelId, string queueName, ISubscriberListener listener)
    {
        _client = client;
        ChannelId = channelId;
        QueueName = queueName;
        _listener = listener;
    }

    public async Task CloseAsync()
    {
        _client.RemoveSubscriber(ChannelId);
        await _client.SendMessageAsync(ChannelId, "CLOSE");
    }

    internal async Task<bool> HandleMessageAsync(string message)
    {
        if (message == "ERR")
        {
            await CloseAsync();
            await _listener.OnErrorAsync();
            return true;
        }

        string[] fields = message.Split(':', 3);
        if (fields.Length != 3)
        {
            return false;
        }

        if (!long.TryParse(fields[0], out long timestamp) || timestamp < 0)
        {
            return false;
        }

        string type = fields[1];
        string data = fields[2];

        await _listener.OnEventAsync(new SubscriberEvent(timestamp, type, data));
        return true;
    }

    internal async Task ErrorAsync()
        => await _listener.OnErrorAsync();
}