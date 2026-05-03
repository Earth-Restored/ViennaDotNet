using System.Collections.Immutable;
using System.Reflection;

namespace Solace.LauncherUI;

internal static class Permissions
{
    [PermissionInfo("Server", "Start the server")]
    public const string StartServer = "server.start";
    [PermissionInfo("Server", "Restart the server")]
    public const string RestartServer = "server.restart";
    [PermissionInfo("Server", "Stop the server")]
    public const string StopServer = "server.stop";
    [PermissionInfo("Server", "Edit the server options")]
    public const string EditServerOptions = "server.options.edit";
    [PermissionInfo("Server", "View the logs of the server")]
    public const string ViewServerLogs = "server.view.logs";

    [PermissionInfo("Users", "Manage roles - add, edit, delete")]
    public const string EditRoles = "user.role.edit";
    [PermissionInfo("Users", "View all users accounts")]
    public const string ViewUsers = "user.view";
    [PermissionInfo("Users", "Assign and remove roles to/from users")]
    public const string AssignRoles = "user.role.assign";
    [PermissionInfo("Users", "Delete user accounts")]
    public const string DeleteUsers = "user.delete";
    [PermissionInfo("Users", "Edit user account info")]
    public const string EditAcountInfo = "user.edit";

    [PermissionInfo("Players", "View all player accounts")]
    public const string ViewPlayers = "player.view";
    [PermissionInfo("Players", "Manage player accounts - edit, delete")]
    public const string ManagePlayers = "player.manage";

    [PermissionInfo("Buildplates", "View the imported buildplates")]
    public const string ViewBuildplates = "buildplate.view";

    [PermissionInfo("Buildplates", "Manage buildplates - import, edit, delete")]
    public const string ManageBuildplates = "buildplate.manage";

    [PermissionInfo("Data", "View server data - space usage")]
    public const string ViewData = "data.view";

    [PermissionInfo("Data", "Import/Export server data")]
    public const string ImportExportData = "data.import_export";

    [PermissionInfo("Data", "Delete all server data")]
    public const string DeleteData = "data.delete";

    public static readonly ImmutableArray<string> All;
    public static readonly ImmutableArray<PermissionDescriptor> AllWithInfo;

    static Permissions()
    {
        var fields = typeof(Permissions)
             .GetFields(BindingFlags.Public | BindingFlags.Static)
             .Where(f => f is { IsLiteral: true, IsInitOnly: false } &&
                         f.FieldType == typeof(string));

        All = [.. fields.Select(f => (string)f.GetRawConstantValue()!)];

        AllWithInfo = [.. fields.Select(f =>
        {
            var attr = f.GetCustomAttribute<PermissionInfoAttribute>();

            return new PermissionDescriptor(
                (string)f.GetRawConstantValue()!,
                attr?.Category ?? "Other",
                attr?.Description ?? ""
            );
        })];
    }

    internal readonly record struct PermissionDescriptor(
        string Name,
        string Category,
        string Description
    );

    [AttributeUsage(AttributeTargets.Field)]
    private sealed class PermissionInfoAttribute : Attribute
    {
        public string Category { get; }
        public string Description { get; }

        public PermissionInfoAttribute(string category, string description)
        {
            Category = category;
            Description = description;
        }
    }
}