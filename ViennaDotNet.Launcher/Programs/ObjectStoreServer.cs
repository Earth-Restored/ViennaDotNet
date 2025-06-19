using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ViennaDotNet.Launcher.Programs;

internal static class ObjectStoreServer
{
    public static readonly string ExeName =  "ObjectStoreServer" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DispName = "ObjectStore server";

    public static bool Check(Settings settings, ILogger logger)
    {
        string exePath = Path.GetFullPath(Path.Combine(Program.ProgramsDir, ExeName));
        if (!File.Exists(exePath))
        {
            logger.Error($"{DispName} executable doesn't exits: {exePath}");
            return false;
        }

        return true;
    }

    public static void Run(Settings settings, ILogger logger)
    {
        logger.Information($"Running {DispName}");
        Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(Program.ProgramsDir, ExeName)),
        [
            $"--dataDir=data{Path.DirectorySeparatorChar}objecct_store",
            $"--port={settings.ObjectStorePort}"
        ])
        {
            WorkingDirectory = Path.Combine(Environment.CurrentDirectory, Program.ProgramsDir),
            CreateNoWindow = false,
            UseShellExecute = true
        });
    }
}
