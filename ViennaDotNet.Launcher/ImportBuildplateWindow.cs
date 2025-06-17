using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using ViennaDotNet.Launcher.Programs;
using ViennaDotNet.Launcher.Utils;

namespace ViennaDotNet.Launcher;

internal sealed class ImportBuildplateWindow : Window
{
    public ImportBuildplateWindow(Settings settings)
    {
        const int ChoiceButtonsGroup = 0;

        Title = "Import buidplate";

        var idLabel = new Label()
        {
            Text = "Player ID:",
        };

        var idInput = new TextField()
        {
            X = Pos.Right(idLabel) + 1,
            Y = Pos.Y(idLabel),
            Width = Dim.Fill(),
        };

        var worldFileLabel = new Label()
        {
            Text = "Buildplate file:",
            X = Pos.X(idLabel),
            Y = Pos.Bottom(idLabel) + 1,
        };
        var worldFileInput = new Button
        {
            Text = "Not selected",
            X = Pos.Right(worldFileLabel) + 1,
            Y = Pos.Y(worldFileLabel),
        };
        worldFileInput.Accepting += (s, e) =>
        {
            var dialog = new OpenDialog()
            {
                AllowedTypes = [new AllowedType("Vienna", ".zip"), new AllowedType("Project Earth", ".json")],
                AllowsMultipleSelection = false,
                MustExist = true,
                ShadowStyle = ShadowStyle.Transparent,
                OpenMode = OpenMode.File,
            };

            Application.Run(dialog);

            if (dialog.FilePaths.Count == 0)
            {
                worldFileInput.Text = "Not selected";
            }
            else
            {
                worldFileInput.Text = dialog.FilePaths[0];
            }

            e.Handled = true;
        };

        var cancelBtn = new Button()
        {
            Text = "Cancel",
            X = Pos.Align(Alignment.Center, AlignmentModes.AddSpaceBetweenItems, ChoiceButtonsGroup),
            Y = Pos.AnchorEnd(),
        };
        cancelBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            Application.RequestStop();
        };

        var importBtn = new Button()
        {
            Text = "Import",
            X = Pos.Align(Alignment.Center, AlignmentModes.AddSpaceBetweenItems, ChoiceButtonsGroup),
            Y = Pos.AnchorEnd(),
        };
        importBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            string playerId = idInput.Text.ToLowerInvariant();
            string? file = worldFileInput.Text is "Not selected" ? null : worldFileInput.Text;

            if (string.IsNullOrEmpty(file))
            {
                MessageBox.ErrorQuery("Error", "Buildplate file not selected", "OK");
            }
            else if (!File.Exists(file) || Path.GetExtension(file) is not (".zip" or ".json"))
            {
                MessageBox.ErrorQuery("Error", "Invalid buildplate file", "OK");
            }
            else if (string.IsNullOrWhiteSpace(playerId) || playerId.Length == 0)
            {
                MessageBox.ErrorQuery("Error", "Invalid player id", "OK");
            }
            else
            {
                Import(playerId, file);
            }
        };

        Add(idLabel, idInput, worldFileLabel, worldFileInput, cancelBtn, importBtn);
    }

    private void Import(string playerId, string file)
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
            throw new NotImplementedException();
        }
        catch (Exception ex)
        {
            logger.Error($"Exception: {ex}");
        }

        btn.Text = "_OK";
    }
}
