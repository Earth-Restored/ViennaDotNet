using Serilog;
using System.Diagnostics;

namespace ViennaDotNet.Launcher.Programs
{
    internal static class TappablesGenerator
    {
        public const string DirName = "TappablesGenerator";
        public const string ExeName = "TappablesGenerator.exe";
        public const string DispName = "Tappable generator";

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

        public static void Run(Settings settings)
        {
            Log.Information($"Running {DispName}");
            Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(DirName, ExeName)), new string[]
            {
                $"--eventbus=localhost:{settings.EventBusPort}"
            })
            {
                WorkingDirectory = Path.Combine(Environment.CurrentDirectory, DirName),
                CreateNoWindow = false,
                UseShellExecute = true
            });
        }
    }
}
