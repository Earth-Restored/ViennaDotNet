namespace Solace.Common.Excceptions;

public class UnsupportedOperationException : Exception
{
    public UnsupportedOperationException()
        : base()
    {

    }
    public UnsupportedOperationException(string? message)
        : base(message)
    {

    }
}
