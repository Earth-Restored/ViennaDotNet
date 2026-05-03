using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Serilog;
using Solace.Common.Utils;

namespace Solace.Common;

// from https://stackoverflow.com/a/50311340/15878562
public sealed class ConsoleProcess : IDisposable
{
    private readonly string _filePath;
    public readonly Process Process = new Process();

    public bool IORedirected { get; private set; }
    public bool OpenInNewWindow { get; private set; }

    public event DataReceivedEventHandler? ErrorTextReceived
    {
        add => Process.ErrorDataReceived += value;
        remove => Process.ErrorDataReceived -= value;
    }
    public event EventHandler? ProcessExited;
    public event DataReceivedEventHandler? StandartTextReceived
    {
        add => Process.OutputDataReceived += value;
        remove => Process.OutputDataReceived -= value;
    }

    public int? ExitCode
    {
        get
        {
            try
            {
                return ActualProcess.ExitCode;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public string ExitCodeText => ExitCode is { } exitCode ? exitCode.ToString(CultureInfo.InvariantCulture) : "Unknown"; // TODO 

    public int Id => _actualAppPid is null ? Process.Id : _actualAppPid.Value;

    private bool running;

    private string? _pidFilePath;
    private int? _actualAppPid;
    private Process? _cachedActualProcess;

    private Process ActualProcess
    {
        get
        {
            if (_actualAppPid is null)
            {
                return Process;
            }

            if (_cachedActualProcess is not null)
            {
                return _cachedActualProcess;
            }

            try
            {
                _cachedActualProcess = Process.GetProcessById(_actualAppPid.Value);
                return _cachedActualProcess;
            }
            catch (ArgumentException)
            {
                return Process;
            }
        }
    }

    public ConsoleProcess(string appName, bool useShellExecute, bool redirect, bool openInNewWindow = false)
    {
        if (openInNewWindow && redirect)
        {
            throw new InvalidOperationException("Standard I/O cannot be redirected when opening in a new window.");
        }

        if (redirect && useShellExecute)
        {
            throw new InvalidOperationException("Can't redirect std in/out when useShellExecute is true");
        }

        if (OperatingSystem.IsLinux() && openInNewWindow && !IsXTerminalEmulatorPresent())
        {
            openInNewWindow = false;
        }

        _filePath = appName;
        IORedirected = redirect;
        OpenInNewWindow = openInNewWindow;

        Process.StartInfo = new ProcessStartInfo(appName)
        {
            RedirectStandardError = redirect,
            RedirectStandardInput = redirect,
            RedirectStandardOutput = redirect,
            UseShellExecute = useShellExecute,
            CreateNoWindow = !useShellExecute && !openInNewWindow,
        };

        Process.EnableRaisingEvents = true;

        Process.Exited += ProcessOnExited;
    }

    public async Task ExecuteAsync(string? workingDir, params string[] args)
    {
        if (running)
        {
            throw new InvalidOperationException("Process is still Running. Please wait for the process to complete.");
        }

        if (!string.IsNullOrEmpty(workingDir))
        {
            Process.StartInfo.WorkingDirectory = workingDir;
        }

        if (OpenInNewWindow && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ApplyTerminalWrapper(args);
        }
        else
        {
            Process.StartInfo.Arguments = FormatStandardArguments(args);
        }

        Process.Start();
        running = true;

        if (_pidFilePath != null)
        {
            _actualAppPid = await ResolveActualPidAsync(_pidFilePath);
        }

        if (IORedirected)
        {
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
        }
    }

    public void Write(string data)
    {
        if (!IORedirected)
        {
            throw new InvalidOperationException($"Can't write, because {nameof(IORedirected)} is false");
        }

        if (data is null)
        {
            return;
        }

        Process.StandardInput.Write(data);
        Process.StandardInput.Flush();
    }

    public void WriteLine(string data)
        => Write(data + Environment.NewLine);

    private static string FormatStandardArguments(IEnumerable<string> args)
    {
        var formattedArgs = args.Select(a =>
        {
            if (string.IsNullOrEmpty(a))
            {
                return "\"\"";
            }

            if (a.Contains(' ') || a.Contains('{') || a.Contains('"'))
            {
                return $"\"{a.Replace("\"", "\\\"")}\"";
            }

            return a;
        });

        return string.Join(" ", formattedArgs);
    }

    private void OnProcessExited()
        => ProcessExited?.Invoke(this, EventArgs.Empty);

    private void ProcessOnExited(object? sender, EventArgs eventArgs)
        => OnProcessExited();

    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
        => await ActualProcess.WaitForExitAsync(cancellationToken);

    public async Task StopNoWaitAsync(int timeout = 15 * 1000, CancellationToken cancellationToken = default)
        => await ActualProcess.StopGracefullyOrKillAsync(timeout, cancellationToken);

    public async Task StopAndWaitAsync(int timeout = 15 * 1000, CancellationToken cancellationToken = default)
        => await ActualProcess.StopGracefullyOrKillAndWaitAsync(timeout, cancellationToken);

    private void ApplyTerminalWrapper(IEnumerable<string> args)
    {
        Process.StartInfo.UseShellExecute = true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.StartInfo.FileName = "x-terminal-emulator";

            var linuxArgs = args.Select(a => $"'{a.Replace("'", "'\\''")}'");

            string innerCommand = $"'{_filePath.Replace("'", "'\\''")}' {string.Join(" ", linuxArgs)}";

            innerCommand = innerCommand
                .Replace("\\", "\\\\")
                .Replace("$", "\\$")
                .Replace("\"", "\\\"");

            _pidFilePath = Path.GetTempFileName();

            Process.StartInfo.Arguments = $"-e bash -c \"echo $$ > '{_pidFilePath}'; exec {innerCommand}\"";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // todo: currently not tested
            string arguments = FormatStandardArguments(args);
            string command = $"'{_filePath}' {arguments}";
            string appleScript = $"tell application \"Terminal\" to do script \"{command.Replace("\"", "\\\"")}; exit\"";

            Process.StartInfo.FileName = "osascript";
            Process.StartInfo.Arguments = $"-e \"{appleScript}\"";
        }
        else
        {
            Debug.Fail("Unsupported platform");
        }
    }

    private static bool IsXTerminalEmulatorPresent()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = "-c \"command -v x-terminal-emulator\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            // exit code 0 - command was found
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int?> ResolveActualPidAsync(string pidFile, int timeout = 5000)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var content = await File.ReadAllTextAsync(pidFile, cts.Token);
                if (int.TryParse(content.Trim(), out int pid))
                {
                    // Clean up the temp file
                    File.Delete(pidFile);
                    return pid;
                }
            }
            catch (IOException)
            {
            }

            await Task.Delay(100, cts.Token);
        }

        if (File.Exists(pidFile))
        {
            File.Delete(pidFile);
        }

        return null;
    }

    public void Dispose()
    {
        Process.Dispose();
        _cachedActualProcess?.Dispose();
    }
}
