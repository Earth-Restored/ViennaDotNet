namespace ViennaDotNet.EventBus.Client;

public class EventBusClientException : Exception
{
    public EventBusClientException(string? message)
        : base(message)
    {
    }

    public EventBusClientException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
