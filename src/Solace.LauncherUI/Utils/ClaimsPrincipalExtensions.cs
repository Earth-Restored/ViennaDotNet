using System.Security.Claims;

namespace Solace.LauncherUI.Utils;

public static class ClaimsPrincipalExtensions
{
    extension (ClaimsPrincipal principal)
    {
        public bool HasPermission(string permission)
            => principal?.HasClaim("Permission", permission) ?? false;
    }
}