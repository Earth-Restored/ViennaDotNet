namespace ViennaDotNet.ApiServer.Exceptions;

public class ServerErrorException : Exception
{
    public ServerErrorException()
        : base()
    {
    }
    public ServerErrorException(string? message)
        : base(message)
    {
    }
    public ServerErrorException(Exception? innerException)
        : base(null, innerException)
    {
    }
    public ServerErrorException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
