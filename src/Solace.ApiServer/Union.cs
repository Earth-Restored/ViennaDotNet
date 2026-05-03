using System.Diagnostics.CodeAnalysis;

namespace Solace.ApiServer;

public readonly struct Union<TA, TB>
    where TA : notnull
    where TB : notnull
{
    private readonly object _value;

    internal Union(object value, bool isB)
    {
        _value = value;
        IsB = isB;
    }

    [MemberNotNullWhen(false, nameof(A))]
    [MemberNotNullWhen(true, nameof(B))]
    public bool IsB { get; }

    public TA? A => (TA)_value;

    public TB? B => (TB)_value;

    public static implicit operator Union<TA, TB>(TA value)
        => Union.CreateA<TA, TB>(value);

    public static implicit operator Union<TA, TB>(TB value)
        => Union.CreateB<TA, TB>(value);
}

public static class Union
{
    public static Union<TA, TB> CreateA<TA, TB>(TA value)
        where TA : notnull
        where TB : notnull
        => new Union<TA, TB>(value, false);

    public static Union<TA, TB> CreateB<TA, TB>(TB value)
        where TA : notnull
        where TB : notnull
        => new Union<TA, TB>(value, true);
}
