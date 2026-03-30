using Serilog;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.LauncherUI.Programs;
using ViennaDotNet.LauncherUI.Utils;

namespace ViennaDotNet.LauncherUI;

public class ServerManager
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

    private static readonly IEnumerable<string> programExes = [TileRenderer.ExeName, TappablesGenerator.ExeName, ApiServer.ExeName, BuildplateLauncher.ExeName, ObjectStoreServer.ExeName, EventBusServer.ExeName];

    private readonly Lock _statusLock = new Lock();

    private CancellationTokenSource? _operationTokenSource;

    public async Task Start(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            if (Status is not ServerStatus.Offline)
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
            if (Status is ServerStatus.Offline or ServerStatus.Stopping)
            {
                return;
            }

            cancellationToken = InitOperation(cancellationToken);

            Status = ServerStatus.Stopping;
        }

        foreach (string programName in programExes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StopProgram(programName, Log.Logger, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        Status = ServerStatus.Offline;
    }

    public async Task Restart(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            if (Status is ServerStatus.Stopping or ServerStatus.Stopping)
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

        foreach (string programName in programExes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StopProgram(programName, Log.Logger, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Status = ServerStatus.Offline;
    }

    private async Task StartInternal(Serilog.ILogger logger, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Status = ServerStatus.Starting;
        var settings = Settings.Instance;

        if (!await FileChecker.CheckAsync(settings, false, logger, cancellationToken))
        {
            Log.Error("File validation failed");
            Status = ServerStatus.Offline;
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        EventBusServer.Run(settings, logger);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectStoreServer.Run(settings, logger);

        await Task.Delay(1500, cancellationToken); // required for next components, wait for startup

        BuildplateLauncher.Run(settings, logger);

        await Task.Delay(1500, cancellationToken); // required for api server preview generation, wait for startup

        ApiServer.Run(settings, logger);
        cancellationToken.ThrowIfCancellationRequested();
        TappablesGenerator.Run(settings, logger);
        if (settings.EnableTileRenderingLabel ?? true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TileRenderer.Run(settings, logger);
        }

        logger.Information("Waiting for programs to start up");
        await Task.Delay(7500, cancellationToken); // wait a bit for them to start (and possible crash)

        bool error = false;
        foreach (string programExe in programExes)
        {
            if (!ProcessUtils.GetProgramProcesses(programExe).Any() && (programExe != TileRenderer.ExeName || (settings.EnableTileRenderingLabel ?? true)))
            {
                logger.Error($"It was detected that {programExe} crashed/exited, make sure all options are set correctly, look into logs/[program name]/logxxx for more info");
                error = true;
            }
        }

        if (!error)
        {
            logger.Information("All programs have (most likely) started succesfully");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Status = ServerStatus.Online;
    }

    private static async Task StopProgram(string name, Serilog.ILogger logger, CancellationToken cancellationToken)
    {
        logger.Information($"Stopping {name}");

        int stoppedCount = 0;
        foreach (var process in ProcessUtils.GetProgramProcesses(name))
        {
            await process.StopGracefullyOrKillAsync(3000, false, cancellationToken);
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