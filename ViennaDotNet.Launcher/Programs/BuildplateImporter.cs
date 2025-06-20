using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ViennaDotNet.Common;

namespace ViennaDotNet.Launcher.Programs;

internal static class BuildplateImporter
{
    public static readonly string ExeName = "BuildplateImporter" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DispName = "Buildplate importer";

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

    public static Process Run(Settings settings, string playerId, string filePath, bool redirectOutput, ILogger logger)
    {
        logger.Information($"Running {DispName}");
        ConsoleProcess process;
        try
        {
            process = new ConsoleProcess(Path.GetFullPath(Path.Combine(Program.ProgramsDir, ExeName)), false, redirectOutput);
            if (!redirectOutput)
            {
                process.StandartTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        logger.Debug($"[importer] {e.Data}");
                    }
                };
                process.ErrorTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        logger.Error($"[importer] {e.Data}");
                    }
                };
            }

            process.ExecuteAsync(Path.GetFullPath(Program.ProgramsDir), [
                $"--db={settings.EarthDatabaseConnectionString}",
                $"--eventbus=localhost:{settings.EventBusPort}",
                $"--objectstore=localhost:{settings.ObjectStorePort}",
                $"--id={playerId}",
                $"--file={filePath}"
            ]);
        }
        catch (Exception ex)
        {
            logger.Error($"Error starting {DispName} process: {ex}");
            return null;
        }

        return process.Process;
    }
}
