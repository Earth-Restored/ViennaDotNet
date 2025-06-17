using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ViennaDotNet.Launcher.Programs;

internal static class BuildplateImporter
{
    public const string DirName = "BuildplateImporter";
    public static readonly string ExeName = "Buildplate_Importer" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DispName = "Buildplate importer";

    public static bool Check()
    {
        string exePath = Path.GetFullPath(Path.Combine(DirName, ExeName));
        if (!File.Exists(exePath))
        {
            Log.Error($"{DispName} exe doesn't exits: {exePath}");
            return false;
        }

        return true;
    }

    public static int? Run(Settings settings, string playerId, string filePath)
    {
        Log.Information($"Running {DispName}");
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(DirName, ExeName)),
            [
                $"--db={settings.EarthDatabaseConnectionString}",
                $"--eventbus=localhost:{settings.EventBusPort}",
                $"--objectstore=localhost:{settings.ObjectStorePort}",
                $"--id={playerId}",
                $"--file={filePath}"
            ])
            {
                WorkingDirectory = Path.Combine(Environment.CurrentDirectory, DirName),
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Error starting importer process: {ex}");
            return null;
        }

        if (process is null)
        {
            Log.Error("Importer process failed to start");
            return null;
        }

        process.WaitForExit();

        return process.ExitCode;
    }
}
