using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Launcher.Programs
{
    internal static class BuildplateImporter
    {
        public const string DirName = "Buildplate_Importer";
        public const string ExeName = "ViennaDotNet.Buildplate_Importer.exe";
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

        public static int? Run(Settings settings, string playerId, string worldPath)
        {
            Log.Information($"Running {DispName}");
            Process? process;
            try
            {
                process = Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(DirName, ExeName)), new string[]
                {
                    $"--db={settings.DatabaseConnectionString}",
                    $"--eventbus=localhost:{settings.EventBusPort}",
                    $"--objectstore=localhost:{settings.ObjectStorePort}",
                    $"--id={playerId}",
                    $"--file={worldPath}"
                })
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
}
