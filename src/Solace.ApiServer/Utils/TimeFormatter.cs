using System.Globalization;

namespace ViennaDotNet.ApiServer.Utils;

public static class TimeFormatter
{
    private static readonly string JSON_DATE_FORMAT = "yyyy-MM-ddTHH:mm:ssZ";

    public static string FormatTime(long time)
    {
        DateTime dateTime = DateTimeOffset.FromUnixTimeMilliseconds(time).UtcDateTime;
        return FormatTime(dateTime);
    }
    public static string FormatTime(DateTime dateTime)
        => dateTime.ToString(JSON_DATE_FORMAT, CultureInfo.InvariantCulture);

    public static string FormatDuration(long duration)
    {
        TimeSpan timeSpan = TimeSpan.FromMilliseconds(duration);
        return FormatDuration(timeSpan);
    }
    public static string FormatDuration(TimeSpan timeSpan)
        => $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";

    public static long ParseDuration(string duration)
    {
        string[] parts = duration.Split(':');
        if (parts.Length < 3)
            throw new ArgumentException("Invalid duration format");

        long hours = long.Parse(parts[0]);
        long minutes = long.Parse(parts[1]);
        long seconds = long.Parse(parts[2]);

        return (hours * 3600 + minutes * 60 + seconds) * 1000L;
    }
}
