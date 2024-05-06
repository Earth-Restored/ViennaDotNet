using Serilog;
using System.Diagnostics;

namespace ViennaDotNet.Launcher.Programs
{
    internal static class Buildplate
    {
        public const string DirName = "Buildplate";
        public const string ExeName = "BuildplateLauncher.exe";
        public const string DispName = "Buildplate launcher";

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

        public static void Run(Settings settings, string bridgeJar, string serverTemplateDir, string fabricJarName, string connectorPluginJar)
        {
            Log.Information($"Running {DispName}");
            Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(DirName, ExeName)), new string[]
            {
                $"--eventbus=localhost:{settings.EventBusPort}",
                $"--publicAddress={settings.IPv4}",
                $"--bridgeJar={bridgeJar}",
                $"--serverTemplateDir={serverTemplateDir}",
                $"--fabricJarName={fabricJarName}",
                $"--connectorPluginJar={connectorPluginJar}"
            })
            {
                WorkingDirectory = Path.Combine(Environment.CurrentDirectory, DirName),
                CreateNoWindow = false,
                UseShellExecute = true
            });
        }
    }
}
