using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ViennaDotNet.LauncherUI.Utils;

internal static class ProcessUtils
{
    public static IEnumerable<Process> GetProgramProcesses(string name)
    {
        string exePath = Path.GetFullPath(Path.Join(Program.ProgramsDir, name));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Debug.Assert(name.EndsWith(".exe", StringComparison.Ordinal));
            name = name[..^4];
        }

        foreach (var process in Process.GetProcessesByName(name))
        {
            if (process.MainModule is null || process.MainModule.FileName != exePath)
            {
                continue;
            }

            yield return process;
        }
    }

    public static Process? StartIfNotRunning(string name, Func<Process?> start)
        => GetProgramProcesses(name).FirstOrDefault() ?? start();
}
