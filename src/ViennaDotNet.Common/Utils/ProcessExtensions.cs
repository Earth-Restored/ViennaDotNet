using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace ViennaDotNet.Common.Utils;

public static partial class ProcessExtensions
{
    public static void StopGracefullyOrKill(this Process process, int timeout, bool allowConsoleReAlloc = false)
    {
        if (!process.TryStopGracefully(timeout, allowConsoleReAlloc))
        {
            process.Kill();
        }

        process.WaitForExit(timeout);
    }

    public static async Task StopGracefullyOrKillAsync(this Process process, int timeout, bool allowConsoleReAlloc, CancellationToken cancellationToken)
    {
        if (!await process.TryStopGracefullyAsync(timeout, allowConsoleReAlloc, cancellationToken))
        {
            process.Kill();
        }

        await process.WaitForExitAsync(timeout, cancellationToken);
    }

    public static bool TryStopGracefully(this Process process, int timeout, bool allowConsoleReAlloc)
    {
        try
        {
            if (process.TryCloseMainWindow(timeout))
            {
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && allowConsoleReAlloc)
            {
                if (process.WinTrySendCtrlC(timeout))
                {
                    return true;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // TODO: test if this actually works
                if (process.UnixTrySendShutdownSignal(timeout))
                {
                    return true;
                }
            }
        }
        catch { }

        return process.HasExited;
    }

    public static async Task<bool> TryStopGracefullyAsync(this Process process, int timeout, bool allowConsoleReAlloc, CancellationToken cancellationToken)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && allowConsoleReAlloc)
            {
                if (await process.TryCloseMainWindowAsync(timeout, cancellationToken))
                {
                    return true;
                }

                if (await process.WinTrySendCtrlCAsync(timeout, cancellationToken))
                {
                    return true;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // TODO: test if this actually works
                if (await process.UnixTrySendShutdownSignalAsync(timeout, cancellationToken))
                {
                    return true;
                }
            }
        }
        catch { }

        return process.HasExited;
    }

    public static Task WaitForExitAsync(this Process process, int timeout, CancellationToken cancellationToken)
        => Task.WhenAny(process.WaitForExitAsync(cancellationToken), Task.Delay(timeout, cancellationToken));

    #region Sync
    private static bool WinTrySendCtrlC(this Process process, int timeout)
    {
        Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        FreeConsole(); // free our console
        if (AttachConsole((uint)process.Id))
        {
            SetConsoleCtrlHandler(null, true);
            try
            {
                if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0))
                {
                    return false;
                }
            }
            finally
            {
                SetConsoleCtrlHandler(null, false);
                FreeConsole();
                ConsoleUtils.CreateConsole(true);
                Console.WriteLine("Re allocated/attached console");
            }

            try
            {
                process.WaitForExit(timeout);
            }
            catch { }

            return process.HasExited;
        }
        else
        {
            ConsoleUtils.CreateConsole(true);
            return false;
        }
    }

    private static bool UnixTrySendShutdownSignal(this Process process, int timeout)
    {
        try
        {
            var killProc = Process.Start("kill", $"-s {process.UnixGetSignal()} {process.Id}");
            killProc.WaitForExit(1000);
            Debug.Assert(killProc.HasExited);

            process.WaitForExit(timeout);
        }
        catch { }

        return process.HasExited;
    }

    private static string UnixGetSignal(this Process process)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string fd0Path = $"/proc/{process.Id}/fd/0";
            try
            {
                var fileInfo = new FileInfo(fd0Path);

                FileSystemInfo? target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);

                if (target is not null && target.FullName.Contains("/dev/tty", StringComparison.Ordinal))
                {
                    return "INT";
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not resolve link target for process '{process.Id}': {ex.Message}");
            }
        }
        else
        {
            Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
            var psi = new ProcessStartInfo
            {
                FileName = "ps",
                Arguments = $"-o tty= -p {process.Id}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var ps = Process.Start(psi);
            Debug.Assert(ps is not null);

            string tty = ps.StandardOutput.ReadToEnd().Trim();
            ps.WaitForExit();

            if (!string.IsNullOrEmpty(tty) && tty != "?")
            {
                return "INT";
            }
        }

        return "TERM";
    }

    private static bool TryCloseMainWindow(this Process process, int timeout)
    {
        try
        {
            if (!process.CloseMainWindow())
            {
                return false;
            }

            process.WaitForExit(timeout);
        }
        catch { }

        return process.HasExited;
    }
    #endregion
    #region Async
    private static async Task<bool> WinTrySendCtrlCAsync(this Process process, int timeout, CancellationToken cancellationToken)
    {
        Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        FreeConsole(); // free our console
        if (AttachConsole((uint)process.Id))
        {
            SetConsoleCtrlHandler(null, true);
            try
            {
                if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0))
                {
                    return false;
                }
            }
            finally
            {
                SetConsoleCtrlHandler(null, false);
                FreeConsole();
                ConsoleUtils.CreateConsole(true);
                Console.WriteLine("Re allocated/attached console");
            }

            try
            {
                await process.WaitForExitAsync(timeout, cancellationToken);
            }
            catch { }

            return process.HasExited;
        }
        else
        {
            ConsoleUtils.CreateConsole(true);
            return false;
        }
    }

    private static async Task<bool> UnixTrySendShutdownSignalAsync(this Process process, int timeout, CancellationToken cancellationToken)
    {
        try
        {
            var killProc = Process.Start("kill", $"-s {await process.UnixGetSignalAsync(cancellationToken)} {process.Id}");
            killProc.WaitForExit(1000);
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
            if (ps != null)
            {
                // Use the async version of ReadToEnd to avoid blocking the thread
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
