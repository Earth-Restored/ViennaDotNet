using Serilog;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.Launcher.Programs;
using ViennaDotNet.Launcher.Utils;

namespace ViennaDotNet.Launcher;

internal sealed class LauncherWindow : Window
{
    private static readonly IEnumerable<string> programExes = [TileRenderer.ExeName, TappablesGenerator.ExeName, ApiServer.ExeName, BuildplateLauncher.ExeName, ObjectStoreServer.ExeName, EventBusServer.ExeName];

    private static Settings settings => Program.Settings;

    public LauncherWindow()
    {
        Title = "ViennaDotNet Launcher";

        var startBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Absolute(1),
            Text = "_Start",
        };
        startBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            Start(settings);
        };

        var stopBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(startBtn) + 1,
            Text = "_Stop",
        };
        stopBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            Stop();
        };

        var optionsBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(stopBtn) + 1,
            Text = "_Options",
        };
        optionsBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            using var options = new OptionsWindow(settings)
            {
                X = Pos.Center(),
                Y = Pos.Center(),
                //Modal = true,
            };

            Application.Run(options);

            settings.Save(Program.SettingsFile);
        };

        var importBuildplateBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(optionsBtn) + 1,
            Text = "_Import buildplate",
        };
        importBuildplateBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            using var importBuildplate = new ImportBuildplateWindow(settings)
            {
                X = Pos.Center(),
                Y = Pos.Center(),
                //Modal = true,
            };

            Application.Run(importBuildplate);
        };

        var dataBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(importBuildplateBtn) + 1,
            Text = "_Modify data",
        };

        var exitBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(dataBtn) + 1,
            Text = "_Exit",
        };
        exitBtn.Accepting += (s, e) =>
        {
            Application.RequestStop();

            e.Handled = true;
        };

        Add(startBtn, stopBtn, optionsBtn, importBuildplateBtn, dataBtn, exitBtn);
    }

    private void Start(Settings settings)
        => UIUtils.RunWithLogs(this, async (logger, cancellationToken) =>
        {
            await FileChecker.Check(settings, false, logger, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            EventBusServer.Run(settings, logger);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectStoreServer.Run(settings, logger);
            cancellationToken.ThrowIfCancellationRequested();
            ApiServer.Run(settings, logger);
            cancellationToken.ThrowIfCancellationRequested();
            BuildplateLauncher.Run(settings, logger);
            cancellationToken.ThrowIfCancellationRequested();
            TappablesGenerator.Run(settings, logger);
            cancellationToken.ThrowIfCancellationRequested();
            TileRenderer.Run(settings, logger);

            logger.Information("Waiting for programs to start up");
            await Task.Delay(5000, cancellationToken); // wait a bit for them to start (and possible crash)

            bool error = false;
            foreach (string programExe in programExes)
            {
                if (!ProcessUtils.GetProgramProcesses(programExe).Any())
                {
                    logger.Error($"It was detected that {programExe} crashed/exited, make sure all options are set correctly, look into logs/[program name]/logxxx for more info");
                    error = true;
                }
            }

            if (!error)
            {
                logger.Information("All programs have (most likely) started succesfully");
            }
        });

    private void Stop()
    {
        int selected = MessageBox.Query("Confirm", "Are you sure you want to stop all currently runnning server instances?", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ["OK", "Cancel"] : ["Cancel", "OK"]);

        if (selected != (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 0 : 1))
        {
            return;
        }

        UIUtils.RunWithLogs(this, async (logger, cancellationToken) =>
        {
            foreach (string programName in programExes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await StopProgram(programName, logger, cancellationToken);
            }
        });
    }

    private static async Task StopProgram(string name, ILogger logger, CancellationToken cancellationToken)
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
}
