using Uma.Uuid;

namespace Solace.Common.Utils;

public static class U
{
    public static long CurrentTimeMillis()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
