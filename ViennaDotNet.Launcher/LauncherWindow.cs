using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using ViennaDotNet.Launcher.Programs;
using ViennaDotNet.Launcher.Utils;

namespace ViennaDotNet.Launcher;

internal sealed class LauncherWindow : Window
{
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

        var optionsBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(startBtn) + 1,
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

        Add(startBtn, optionsBtn, importBuildplateBtn, dataBtn, exitBtn);
    }

    private void Start(Settings settings)
    {
        var view = new FrameView()
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        var logs = new ObservableCollection<string>();
        var list = new ListView()
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        list.VerticalScrollBar.AutoShow = true;
        list.VerticalScrollBar.Enabled = true;
        list.HorizontalScrollBar.AutoShow = true;
        list.HorizontalScrollBar.Enabled = true;
        list.SetSource(logs);

        var btn = new Button()
        {
            Text = "_Cancel",
            X = Pos.Center(),
            Y = Pos.AnchorEnd(),
        };
        btn.Accepting += (s, e) =>
        {
            e.Handled = true;

            Remove(view);
        };

        view.Add(list, btn);
        Add(view);

        var logger = Program.LoggerConfiguration
            .WriteTo.Collection(logs)
            .CreateLogger();

        try
        {
            if (settings.SkipFileChecks is not true)
            {
                Check(settings, logger);
            }
            else
            {
                logger.Warning("Skipped file validation, you can turn it back on in 'Configure/Skip file validation before starting'");
            }

            EventBusServer.Run(settings, logger);
            ObjectStoreServer.Run(settings, logger);

            Thread.Sleep(1000); // wait a bit for them to start

        }
        catch (Exception ex)
        {
            logger.Error($"Exception: {ex}");
        }

        btn.Text = "_OK";
    }

    private static void Check(Settings settings, ILogger logger)
    {
        Debug.Assert(settings.SkipFileChecks is not true);

        if (!EventBusServer.Check(settings, logger) ||
            !ObjectStoreServer.Check(settings, logger))
        {
            throw new Exception("File validation failed.");
        }
    }
}
