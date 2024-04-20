using System.Globalization;

namespace ViennaDotNet.ApiServer.Utils
{
    public static class TimeFormatter
    {
        private static readonly string JSON_DATE_FORMAT = "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'";
        private static readonly string JSON_DURATION_FORMAT = "{0}:{1:D2}:{2:D2}";

        public static string FormatTime(long time)
        {
            DateTime dateTime = DateTimeOffset.FromUnixTimeMilliseconds(time).UtcDateTime;
            return dateTime.ToString(JSON_DATE_FORMAT, CultureInfo.InvariantCulture);
        }

        public static string FormatDuration(long duration)
        {
            TimeSpan timeSpan = TimeSpan.FromMilliseconds(duration);
            return string.Format(JSON_DURATION_FORMAT, (int)timeSpan.TotalHours, timeSpan.Minutes, timeSpan.Seconds);
        }

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
}
