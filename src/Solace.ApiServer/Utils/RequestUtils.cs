namespace ViennaDotNet.ApiServer.Utils;

internal static class RequestUtils
{
    public static long GetTimestamp(this HttpContext context)
        => ((DateTimeOffset)context.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();
}
