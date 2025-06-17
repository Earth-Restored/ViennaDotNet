using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ViennaDotNet.Launcher.Programs;

internal static class ObjectStoreServer
{
    public const string DirName = "ObjectStoreServer";
    public static readonly string ExeName =  "ObjectStoreServer" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DispName = "ObjectStore server";

    public static bool Check(Settings settings, ILogger logger)
    {
        string exePath = Path.GetFullPath(Path.Combine(DirName, ExeName));
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
        Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(DirName, ExeName)),
        [
            $"--dataDir=data",
            $"--port={settings.ObjectStorePort}"
        ])
        {
            WorkingDirectory = Path.Combine(Environment.CurrentDirectory, DirName),
            CreateNoWindow = false,
            UseShellExecute = true
        });
    }
}
