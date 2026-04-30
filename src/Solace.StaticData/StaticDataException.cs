namespace ViennaDotNet.StaticData;

public sealed class StaticDataException : Exception
{
    public StaticDataException()
    {
    }

    public StaticDataException(string? message)
        : base(message)
    {
    }

    public StaticDataException(string? message, Exception inner)
        : base(message, inner)
    {
    }
}
