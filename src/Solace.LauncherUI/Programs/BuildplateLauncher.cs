using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ILogger = Serilog.ILogger;

namespace Solace.LauncherUI.Programs;

internal static class BuildplateLauncher
{
    public static readonly string ExeName = "BuildplateLauncher" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DispName = "Buildplate launcher";

    public const string ServerJarName = "fabric-server-mc.1.20.4-loader.0.15.10-launcher.1.0.1.jar";

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
            $"--eventbus=localhost:{settings.EventBusPort}",
            $"--publicAddress={settings.IPv4}",
            $"--bridgeJar={Path.GetFullPath(Path.Combine(Program.StaticDataDir, "server_jars", "fountain-0.0.1-SNAPSHOT-jar-with-dependencies.jar"))}",
            $"--serverTemplateDir={Path.GetFullPath(Path.Combine(Program.StaticDataDir, "server_template_dir"))}",
            $"--fabricJarName={ServerJarName}",
            $"--connectorPluginJar={Path.GetFullPath(Path.Combine(Program.StaticDataDir, "server_jars", "buildplate-connector-plugin-0.0.1-SNAPSHOT-jar-with-dependencies.jar"))}",
            $"--dir={Program.StaticDataDir}",
            $"--logger-url={Program.LoggerAddress}",
        ])
        {
            WorkingDirectory = Path.GetFullPath(Program.ProgramsDir),
            CreateNoWindow = false,
            UseShellExecute = true,
        });
    }
}
