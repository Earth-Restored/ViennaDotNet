using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ViennaDotNet.ApiServer;

internal readonly struct ValidityDatePair
{
    [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public readonly DateTimeOffset Issued;
    public readonly DateTimeOffset Expires;

    public ValidityDatePair(DateTimeOffset issued, DateTimeOffset expires)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(issued, expires);

        Issued = RoundToSeconds(issued);
        Expires = RoundToSeconds(expires);

        DateTimeOffset RoundToSeconds(DateTimeOffset dto)
        {
            return new DateTimeOffset(dto.Ticks - (dto.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        }
    }

    public readonly DateTime IssuedDT => Issued.UtcDateTime;

    public readonly DateTime ExpiresDT => Expires.UtcDateTime;

    public readonly string IssuedStr => Issued.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    public readonly string ExpiresStr => Expires.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    public static ValidityDatePair Create(int validForMinutes)
        => Create(TimeSpan.FromMinutes(validForMinutes));

    public static ValidityDatePair Create(TimeSpan validFor)
    {
        var now = DateTimeOffset.UtcNow;
        return new ValidityDatePair(now, now + validFor);
    }
}
