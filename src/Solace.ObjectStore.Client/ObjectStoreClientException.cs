namespace Solace.ObjectStore.Client;

public class ObjectStoreClientException : Exception
{
    public ObjectStoreClientException(string? message)
        : base(message)
    {
    }

    public ObjectStoreClientException(Exception? innerException)
        : base(null, innerException)
    {
    }

    public ObjectStoreClientException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
