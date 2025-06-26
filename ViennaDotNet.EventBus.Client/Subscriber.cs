namespace ViennaDotNet.EventBus.Client;

public sealed class Subscriber
{
    private readonly EventBusClient _client;
    private readonly int _channelId;

    private readonly string _queueName;

    private readonly ISubscriberListener _listener;

    internal Subscriber(EventBusClient client, int channelId, string queueName, ISubscriberListener listener)
    {
        _client = client;
        _channelId = channelId;
        _queueName = queueName;
        _listener = listener;
    }

    public void Close()
    {
        _client.RemoveSubscriber(_channelId);
        _client.SendMessage(_channelId, "CLOSE");
    }

    internal async Task<bool> HandleMessage(string message)
    {
        if (message == "ERR")
        {
            Close();
            _listener.Error();
            return true;
        }
        else
        {
            string[] fields = message.Split(':', 3);
            if (fields.Length != 3)
                return false;

            if (!long.TryParse(fields[0], out long timestamp) || timestamp < 0)
                return false;

            string type = fields[1];
            string data = fields[2];

            await _listener.Event(new Event(timestamp, type, data));

            return true;
        }
    }

    internal void Error()
        => _listener.Error();

    public interface ISubscriberListener
    {
        Task Event(Event _event);

        void Error();
    }

    public class SubscriberListener : ISubscriberListener
    {
        public Func<Event, Task>? OnEvent;
        public Action? OnError;

        public SubscriberListener()
        {
        }
        public SubscriberListener(Func<Event, Task>? onEvent = null, Action? onError = null)
        {
            OnEvent = onEvent;
            OnError = onError;
        }

        public void Error()
            => OnError?.Invoke();

        public Task Event(Event _event)
            => OnEvent?.Invoke(_event) ?? Task.CompletedTask;
    }

    public sealed class Event
    {
        public long Timestamp;
        public string Type;
        public string Data;

        internal Event(long timestamp, string type, string data)
        {
            Timestamp = timestamp;
            Type = type;
            Data = data;
        }
    }
}
