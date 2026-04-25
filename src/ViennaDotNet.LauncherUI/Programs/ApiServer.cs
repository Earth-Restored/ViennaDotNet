using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ILogger = Serilog.ILogger;

namespace ViennaDotNet.LauncherUI.Programs;

internal static class ApiServer
{
    public static readonly string ExeName = "ApiServer" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DispName = "Api server";

    public static bool Check(Settings settings, ILogger logger)
    {
        string exePath = Path.GetFullPath(Path.Combine(Program.ProgramsDir, ExeName));
        if (!File.Exists(exePath))
        {
            logger.Error($"{DispName} exe doesn't exits: {exePath}");
            return false;
        }

        return true;
    }

    public static Process? Run(Settings settings, ILogger logger)
    {
        logger.Information($"Running {DispName}");
        return Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(Program.ProgramsDir, ExeName)),
        [
            $"--port={settings.ApiPort}",
            $"--earth-db={settings.EarthDatabaseConnectionString}",
            $"--live-db={settings.LiveDatabaseConnectionString}",
            $"--eventbus=localhost:{settings.EventBusPort}",
            $"--objectstore=localhost:{settings.ObjectStorePort}",
            $"--logger-url={Program.LoggerAddress}",
            $"--dir={Program.StaticDataDir}",
        ])
        {
            WorkingDirectory = Path.GetFullPath(Program.ProgramsDir),
            CreateNoWindow = false,
            UseShellExecute = true,
        });
    }
}
