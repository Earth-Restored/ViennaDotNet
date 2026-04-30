using System.Security.Claims;

namespace ViennaDotNet.LauncherUI.Utils;

public static class ClaimsPrincipalExtensions
{
    extension (ClaimsPrincipal principal)
    {
        public bool HasPermission(string permission)
            => principal?.HasClaim("Permission", permission) ?? false;
    }
}