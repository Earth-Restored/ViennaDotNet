using System.Diagnostics.CodeAnalysis;

namespace ViennaDotNet.ApiServer;

public readonly struct Union<TA, TB>
    where TA : notnull
    where TB : notnull
{
    private readonly object _value;

    private Union(object value, bool isB)
    {
        _value = value;
        IsB = isB;
    }

    [MemberNotNullWhen(false, nameof(A))]
    [MemberNotNullWhen(true, nameof(B))]
    public bool IsB { get; }

    public TA? A => (TA)_value;

    public TB? B => (TB)_value;

    public static Union<TA, TB> CreateA(TA value)
        => new Union<TA, TB>(value, false);

    public static Union<TA, TB> CreateB(TB value)
        => new Union<TA, TB>(value, true);

    public static implicit operator Union<TA, TB>(TA value)
        => CreateA(value);

    public static implicit operator Union<TA, TB>(TB value)
        => CreateB(value);
}
