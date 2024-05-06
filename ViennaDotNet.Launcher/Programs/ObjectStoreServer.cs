using Serilog;
using System.Diagnostics;

namespace ViennaDotNet.Launcher.Programs
{
    internal static class ObjectStoreServer
    {
        public const string DirName = "ObjectStoreServer";
        public const string ExeName = "ObjectStoreServer.exe";
        public const string DispName = "ObjectStore server";

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
                $"--dataDir={settings.ObjectStoreDataDir}",
                $"--port={settings.ObjectStorePort}"
            })
            {
                WorkingDirectory = Path.Combine(Environment.CurrentDirectory, DirName),
                CreateNoWindow = false,
                UseShellExecute = true
            });
        }
    }
}
