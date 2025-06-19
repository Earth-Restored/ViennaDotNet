using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ViennaDotNet.Launcher.Programs;

internal static class EventBusServer
{
    public static readonly string ExeName = "EventBusServer" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DispName = "EventBus server";

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

    public static void Run(Settings settings, ILogger logger)
    {
        logger.Information($"Running {DispName}");
        Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(Program.ProgramsDir, ExeName)),
        [
            $"--port={settings.EventBusPort}"
        ])
        {
            WorkingDirectory = Path.Combine(Environment.CurrentDirectory, Program.ProgramsDir),
            CreateNoWindow = false,
            UseShellExecute = true
        });
    }
}
