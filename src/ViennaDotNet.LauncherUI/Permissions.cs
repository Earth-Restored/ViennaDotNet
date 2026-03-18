using System.Collections.Immutable;
using System.Reflection;

namespace ViennaDotNet.LauncherUI;

public static class Permissions
{
    public const string StartServer = "server.start";
    public const string RestartServer = "server.restart";
    public const string StopServer = "server.stop";

    public const string ManageRoles = "role.manage";

    public static readonly ImmutableArray<string> All;

    static Permissions()
    {
        All = [.. typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];
    }
}