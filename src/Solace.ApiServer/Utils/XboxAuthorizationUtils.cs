namespace ViennaDotNet.ApiServer.Utils;

internal static class XboxAuthorizationUtils
{
    public static (string UserId, string TokenString)? Parse(string? authorization)
    {
        if (authorization is null)
        {
            return null;
        }

        var authorizationSpan = authorization.AsSpan();

        Span<Range> parts1 = stackalloc Range[3];
        int parts1Length = authorizationSpan.Split(parts1, ' ');

        if (parts1Length is not 2 || authorizationSpan[parts1[0]] is not "XBL3.0")
        {
            return null;
        }

        var part1 = authorizationSpan[parts1[1]];

        Span<Range> parts2 = stackalloc Range[3];
        int parts2Length = part1.Split(parts2, '=');

        if (parts2Length is not 2 || part1[parts2[0]] is not "x")
        {
            return null;
        }

        var part2 = part1[parts2[1]];

        Span<Range> parts3 = stackalloc Range[3];
        int parts3Length = part2.Split(parts3, ';');

        if (parts3Length is not 2)
        {
            return null;
        }

        return (part2[parts3[0]].ToString(), part2[parts3[1]].ToString());
    }
}
