using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ViennaDotNet.Launcher;

internal sealed class LauncherWindow : Window
{
    public LauncherWindow()
    {
        Title = "ViennaDotNet Launcher";

        var startBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Absolute(1),
            Text = "Start",
        };

        var exitBtn = new Button()
        {
            X = Pos.Center(),
            Y = Pos.Bottom(startBtn) + 1,
            Text = "Exit",
        };

        exitBtn.Accepting += (s, e) =>
        {
            Application.RequestStop();

            e.Handled = true;
        };

        Add(startBtn, exitBtn);
    }
}
