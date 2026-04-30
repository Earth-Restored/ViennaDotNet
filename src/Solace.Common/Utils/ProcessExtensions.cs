using Serilog;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ViennaDotNet.Common.Utils;

public static partial class ProcessExtensions
{
    public static async Task StopGracefullyOrKillAsync(this Process process, int timeout, CancellationToken cancellationToken)
    {
        if (!await process.TryStopGracefullyAsync(timeout, cancellationToken))
        {
            process.Kill(true);
        }
    }

    public static async Task StopGracefullyOrKillAndWaitAsync(this Process process, int timeout, CancellationToken cancellationToken)
    {
        await process.StopGracefullyOrKillAsync(timeout, cancellationToken);

        await process.WaitForExitAsync(timeout, cancellationToken);
    }

    public static async Task<bool> TryStopGracefullyAsync(this Process process, int timeout, CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (await process.WinTrySendCtrlCAsync(timeout, cancellationToken))
                {
                    return true;
                }

                if (await process.TryCloseMainWindowAsync(timeout, cancellationToken))
                {
                    return true;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (await process.UnixTrySendShutdownSignalAsync(timeout, cancellationToken))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return process.HasExited;
    }

    public static Task WaitForExitAsync(this Process process, int timeout, CancellationToken cancellationToken)
        => Task.WhenAny(process.WaitForExitAsync(cancellationToken), Task.Delay(timeout, cancellationToken));

    #region Async
    private static async Task<bool> WinTrySendCtrlCAsync(this Process process, int timeout, CancellationToken cancellationToken)
    {
        Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        string exePath = Path.GetFullPath("ViennaDotNet.KillHelper.exe");

        var startInfo = new ProcessStartInfo(exePath, [process.Id.ToString()])
        {
            UseShellExecute = true,
            CreateNoWindow = false
        };

        using (var killProcess = Process.Start(startInfo))
        {
            if (killProcess is null)
            {
                Log.Warning("Failed to start killer process");
                return false;
            }

            await killProcess.WaitForExitAsync();
            var exitCode = killProcess.ExitCode;

            if (exitCode is 0)
            {
                await process.WaitForExitAsync(timeout, cancellationToken);
                return process.HasExited;
            }

            Log.Warning($"Killer process exited with code {exitCode}");

            return false;
        }
    }

    private static async Task<bool> UnixTrySendShutdownSignalAsync(this Process process, int timeout, CancellationToken cancellationToken)
    {
        try
        {
            string signal = await process.UnixGetSignalAsync(cancellationToken);

            var killProc = Process.Start("kill", $"-s {signal} {process.Id}");
            await killProc.WaitForExitAsync(1000, cancellationToken);
            Debug.Assert(killProc.HasExited);

            await process.WaitForExitAsync(timeout, cancellationToken);
        }
        catch { }

        return process.HasExited;
    }

    private static async Task<string> UnixGetSignalAsync(this Process process, CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                // We want to see WHERE the symlink points, not read its contents.
                var linkInfo = File.ResolveLinkTarget($"/proc/{process.Id}/fd/0", returnFinalTarget: true);
                string targetPath = linkInfo?.FullName ?? string.Empty;

                if (targetPath.Contains("/dev/tty") || targetPath.Contains("/dev/pts"))
                {
                    return "INT";
                }
            }
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ps",
                Arguments = $"-o tty= -p {process.Id}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var ps = Process.Start(psi);
            if (ps is not null)
            {
                string tty = await ps.StandardOutput.ReadToEndAsync(cancellationToken);
                await ps.WaitForExitAsync(cancellationToken);

                if (!string.IsNullOrWhiteSpace(tty) && !tty.Contains('?'))
                {
                    return "INT";
                }
            }
        }

        return "TERM";
    }

    private static async Task<bool> TryCloseMainWindowAsync(this Process process, int timeout, CancellationToken cancellationToken)
    {
        try
        {
            if (!process.CloseMainWindow())
            {
                return false;
            }

            await process.WaitForExitAsync(timeout, cancellationToken);
        }
        catch { }

        return process.HasExited;
    }
    #endregion

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    private delegate bool ConsoleCtrlDelegate(uint ctrlType);

    private const uint CTRL_C_EVENT = 0;
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
}
