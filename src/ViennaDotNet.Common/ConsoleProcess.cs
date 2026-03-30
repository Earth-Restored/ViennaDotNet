using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.Common;

// from https://stackoverflow.com/a/50311340/15878562
public sealed class ConsoleProcess
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

    public int ExitCode => Process.ExitCode;
    public int Id => Process.Id;

    private bool running;

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

    public void ExecuteAsync(string? workingDir, params string[] args)
    {
        if (running)
        {
            throw new InvalidOperationException("Process is still Running. Please wait for the process to complete.");
        }

        if (!string.IsNullOrEmpty(workingDir))
        {
            Process.StartInfo.WorkingDirectory = workingDir;
        }
        
        if (OpenInNewWindow)
        {
            ApplyTerminalWrapper(args);
        }
        else
        {
            Process.StartInfo.Arguments = FormatStandardArguments(args);
        }

        Process.Start();
        running = true;

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

            if (a.Contains(" ") || a.Contains("{") || a.Contains("\""))
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

    public void StopAndWait(int timeout = 15 * 1000)
        => Process.StopGracefullyOrKill(timeout);

    private void ApplyTerminalWrapper(IEnumerable<string> args)
    {
        Process.StartInfo.UseShellExecute = true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.StartInfo.FileName = "cmd.exe";
            string arguments = FormatStandardArguments(args);
            Process.StartInfo.Arguments = $"/k \"\"{_filePath}\" {arguments}\"";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.StartInfo.FileName = "x-terminal-emulator";

            var linuxArgs = args.Select(a => $"'{a.Replace("'", "'\\''")}'");

            string innerCommand = $"'{_filePath.Replace("'", "'\\''")}' {string.Join(" ", linuxArgs)}; exec bash";

            string safeInnerCommand = innerCommand
                .Replace("\\", "\\\\")
                .Replace("$", "\\$")
                .Replace("\"", "\\\"");

            Process.StartInfo.Arguments = $"-e bash -c \"{safeInnerCommand}\"";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // todo: currently not tested
            string arguments = FormatStandardArguments(args);
            string command = $"'{_filePath}' {arguments}";
            string appleScript = $"tell application \"Terminal\" to do script \"{command.Replace("\"", "\\\"")}\"";

            Process.StartInfo.FileName = "osascript";
            Process.StartInfo.Arguments = $"-e \"{appleScript}\"";
        }
    }
}
