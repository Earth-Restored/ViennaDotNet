using System.Diagnostics;
using Serilog;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.LauncherUI.Programs;
using ViennaDotNet.LauncherUI.Utils;

namespace ViennaDotNet.LauncherUI;

public sealed class ServerComponent
{
    public string Name { get; }
    public string ExeName { get; }
    public Func<Settings, Serilog.ILogger, Process?> StartAction { get; }
    public int StartupDelayMs { get; }
    public Func<Settings, bool> IsEnabled { get; }

    public ServerStatus Status { get; set; } = ServerStatus.Offline;

    public ServerComponent(string name, string exeName, Func<Settings, Serilog.ILogger, Process?> startAction, int startupDelayMs = 0, Func<Settings, bool>? isEnabled = null)
    {
        Name = name;
        ExeName = exeName;
        StartAction = startAction;
        StartupDelayMs = startupDelayMs;
        IsEnabled = isEnabled ?? (_ => true);
    }
}

public sealed class ServerManager
{
    public event Action? OnStatusChanged;

    private ServerStatus _status = ServerStatus.Offline;
    public ServerStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                OnStatusChanged?.Invoke();
            }
        }
    }

    public bool AnyOnline { get; set; }

    public IReadOnlyList<ServerComponent> Components { get; }

    private readonly Lock _statusLock = new Lock();

    private CancellationTokenSource? _operationTokenSource;

    public ServerManager()
    {
        Components =
        [
            new("Event Bus", EventBusServer.ExeName, EventBusServer.Run),
            new("Object Store", ObjectStoreServer.ExeName, ObjectStoreServer.Run, 1000),
            new("Buildplate Launcher", BuildplateLauncher.ExeName, BuildplateLauncher.Run, 1500),
            new("API Server", ApiServer.ExeName, ApiServer.Run),
            new("Tappables Generator", TappablesGenerator.ExeName, TappablesGenerator.Run),
            new("Tile Renderer", TileRenderer.ExeName, TileRenderer.Run, 0, s => s.EnableTileRenderingLabel ?? true)
        ];

        RefreshComponentStatuses();
    }

    public void RefreshComponentStatuses(bool detectRunning = true)
    {
        bool anyOnline = false;
        bool anyOffline = false;
        foreach (var comp in Components)
        {
            bool isRunning = detectRunning ? ProcessUtils.GetProgramProcesses(comp.ExeName).Any() : comp.Status is ServerStatus.Online;
            comp.Status = isRunning ? ServerStatus.Online : ServerStatus.Offline;

            if (!isRunning && comp.IsEnabled(Settings.Instance))
            {
                anyOffline = true;
            }
            else
            {
                anyOnline = true;
            }
        }

        AnyOnline = anyOnline;

        if (!anyOffline && Status is ServerStatus.Offline)
        {
            Status = ServerStatus.Online;
        }
        else
        {
            OnStatusChanged?.Invoke();
        }
    }

    public async Task<bool> EnsureComponentsOnline(params IEnumerable<string> exeNames)
    {
        List<ServerComponent> targets;

        lock (_statusLock)
        {
            // Identify which of our managed components match the requested EXEs
            targets = [.. Components.Where(c => exeNames.Contains(c.ExeName, StringComparer.OrdinalIgnoreCase))];

            if (targets.Count == 0)
            {
                return true;
            }

            if (Status is ServerStatus.Stopping)
            {
                return false;
            }

            if (targets.All(t => t.Status is ServerStatus.Online))
            {
                return true;
            }

            if (Status is ServerStatus.Offline)
            {
                Status = ServerStatus.Starting;
            }
        }

        var logger = Log.Logger;
        var settings = Settings.Instance;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // Safety timeout

        try
        {
            foreach (var comp in targets)
            {
                if (comp.Status is ServerStatus.Online)
                {
                    continue;
                }

                logger.Information($"Page-level initialization: Starting {comp.Name}");

                comp.Status = ServerStatus.Starting;
                OnStatusChanged?.Invoke();

                comp.StartAction(settings, logger);

                if (comp.StartupDelayMs > 0)
                {
                    await Task.Delay(comp.StartupDelayMs, cts.Token);
                }

                if (ProcessUtils.GetProgramProcesses(comp.ExeName).Any())
                {
                    comp.Status = ServerStatus.Online;
                }
                else
                {
                    comp.Status = ServerStatus.Offline;
                    logger.Error($"{comp.Name} failed to start during page-level initialization.");
                }

                OnStatusChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during selective component startup");
            return false;
        }
        finally
        {
            _status = ServerStatus.Offline;
            RefreshComponentStatuses();
        }

        return targets.All(t => t.Status is ServerStatus.Online);
    }

    public async Task Start(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            if (Status is not ServerStatus.Offline and not ServerStatus.Online)
            {
                return;
            }

            cancellationToken = InitOperation(cancellationToken);
            Status = ServerStatus.Starting;
        }

        try
        {
            await StartInternal(Log.Logger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await Stop(default);
        }
    }

    public async Task Stop(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            if ((Status is ServerStatus.Offline && !AnyOnline) || Status is ServerStatus.Stopping)
            {
                return;
            }

            cancellationToken = InitOperation(cancellationToken);
            Status = ServerStatus.Stopping;
        }

        foreach (var comp in Components)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (comp.Status is ServerStatus.Offline)
            {
                continue;
            }

            comp.Status = ServerStatus.Stopping;
            OnStatusChanged?.Invoke();

            await StopProgram(comp.ExeName, Log.Logger, cancellationToken);

            comp.Status = ServerStatus.Offline;
            OnStatusChanged?.Invoke();
        }

        cancellationToken.ThrowIfCancellationRequested();
        AnyOnline = false;
        Status = ServerStatus.Offline;
    }

    public async Task Restart(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            if (Status is ServerStatus.Stopping or ServerStatus.Starting)
            {
                return;
            }
        }

        await Stop(cancellationToken);
        await Start(cancellationToken);
    }

    public async Task KillAll(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            cancellationToken = InitOperation(cancellationToken);
            Status = ServerStatus.Stopping;
        }

        foreach (var comp in Components)
        {
            cancellationToken.ThrowIfCancellationRequested();

            comp.Status = ServerStatus.Stopping;
            OnStatusChanged?.Invoke();

            await StopProgram(comp.ExeName, Log.Logger, cancellationToken);

            comp.Status = ServerStatus.Offline;
            OnStatusChanged?.Invoke();
        }

        cancellationToken.ThrowIfCancellationRequested();
        AnyOnline = false;
        Status = ServerStatus.Offline;
    }

    private async Task StartInternal(Serilog.ILogger logger, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = Settings.Instance;

        if (!await FileChecker.CheckAsync(settings, false, logger, cancellationToken))
        {
            Log.Error("File validation failed");
            Status = ServerStatus.Offline;
            return;
        }

        RefreshComponentStatuses();

        foreach (var comp in Components)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!comp.IsEnabled(settings))
            {
                comp.Status = ServerStatus.Offline;
                continue;
            }

            if (comp.Status is ServerStatus.Online)
            {
                logger.Information($"{comp.Name} is already running.");
                continue;
            }

            comp.Status = ServerStatus.Starting;
            OnStatusChanged?.Invoke();

            comp.StartAction(settings, logger);

            if (comp.StartupDelayMs > 0)
            {
                await Task.Delay(comp.StartupDelayMs, cancellationToken);
            }

            comp.Status = ServerStatus.Online;
            AnyOnline = true;
            OnStatusChanged?.Invoke();
        }

        logger.Information("Waiting for programs to stabilize");
        await Task.Delay(7500, cancellationToken);

        bool error = false;
        foreach (var comp in Components)
        {
            if (!comp.IsEnabled(settings))
            {
                continue;
            }

            if (!ProcessUtils.GetProgramProcesses(comp.ExeName).Any())
            {
                logger.Error($"It was detected that {comp.Name} crashed/exited, make sure all options are set correctly, look into logs/{comp.Name}/logxxx for more info");
                comp.Status = ServerStatus.Offline;
                error = true;
            }
            else
            {
                comp.Status = ServerStatus.Online;
            }
        }

        OnStatusChanged?.Invoke();

        if (!error)
        {
            logger.Information("All required programs have (most likely) running successfully");
        }

        cancellationToken.ThrowIfCancellationRequested();
        AnyOnline = true;
        Status = ServerStatus.Online;
    }

    private static async Task StopProgram(string name, Serilog.ILogger logger, CancellationToken cancellationToken)
    {
        logger.Information($"Stopping {name}");

        int stoppedCount = 0;
        foreach (var process in ProcessUtils.GetProgramProcesses(name))
        {
            await process.StopGracefullyOrKillAndWaitAsync(3000, cancellationToken);
            stoppedCount++;
        }

        logger.Information(stoppedCount switch
        {
            0 => $"No {name} processes found",
            1 => $"Stopped 1 {name} process",
            _ => $"Stopped {stoppedCount} {name} processes",
        });
    }

    private CancellationToken InitOperation(CancellationToken cancellationToken)
    {
        _operationTokenSource?.Cancel();
        _operationTokenSource = null;

        _operationTokenSource = new CancellationTokenSource();
        var combinedSource = CancellationTokenSource.CreateLinkedTokenSource(_operationTokenSource.Token, cancellationToken);
        return combinedSource.Token;
    }
}

public enum ServerStatus
{
    Online = 0,
    Starting,
    Stopping,
    Offline,
}