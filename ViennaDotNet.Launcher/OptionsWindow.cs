using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ViennaDotNet.Launcher;

internal sealed class OptionsWindow : Window
{
    public OptionsWindow(Settings settings)
    {
        const int ChoiceButtonsGroup = 0;

        Title = "Options";

        var apiPortLabel = new Label()
        {
            Text = "Api _port:"
        };

        var apiPortInput = new TextField()
        {
            Text = settings.ApiPort.ToString(),
            X = Pos.Right(apiPortLabel) + 1,
            Y = Pos.Y(apiPortLabel),
            Width = Dim.Fill(),
        };

        var eventBusPortLabel = new Label()
        {
            Text = "_EventBus port:",
            X = Pos.Left(apiPortLabel),
            Y = Pos.Bottom(apiPortLabel) + 1,
        };

        var eventBusPortInput = new TextField()
        {
            Text = settings.EventBusPort.ToString(),
            X = Pos.Right(eventBusPortLabel) + 1,
            Y = Pos.Y(eventBusPortLabel),
            Width = Dim.Fill(),
        };

        var objectStorePortLabel = new Label()
        {
            Text = "_ObjectStore port:",
            X = Pos.Left(eventBusPortLabel),
            Y = Pos.Bottom(eventBusPortLabel) + 1,
        };

        var objectStorePortInput = new TextField()
        {
            Text = settings.ObjectStorePort.ToString(),
            X = Pos.Right(objectStorePortLabel) + 1,
            Y = Pos.Y(objectStorePortLabel),
            Width = Dim.Fill(),
        };

        var thisIPLabel = new Label()
        {
            Text = "_IPv4 (IP of this computer):",
            X = Pos.Left(objectStorePortLabel),
            Y = Pos.Bottom(objectStorePortLabel) + 1,
        };

        var thisIPInput = new TextField()
        {
            Text = settings.IPv4,
            X = Pos.Right(thisIPLabel) + 1,
            Y = Pos.Y(thisIPLabel),
            Width = Dim.Fill(),
        };

        var earthDBConnectionLabel = new Label()
        {
            Text = "Earth _database connection string:",
            X = Pos.Left(thisIPLabel),
            Y = Pos.Bottom(thisIPLabel) + 1,
        };

        var earthDBConnectionInput = new TextField()
        {
            Text = settings.EarthDatabaseConnectionString,
            X = Pos.Right(earthDBConnectionLabel) + 1,
            Y = Pos.Y(earthDBConnectionLabel),
            Width = Dim.Fill(),
        };

        var tileDBConnectionLabel = new Label()
        {
            Text = "_Tile database connection string:",
            X = Pos.Left(earthDBConnectionLabel),
            Y = Pos.Bottom(earthDBConnectionLabel) + 1,
        };

        var tileDBConnectionInput = new TextField()
        {
            Text = settings.TileDatabaseConnectionString,
            X = Pos.Right(tileDBConnectionLabel) + 1,
            Y = Pos.Y(tileDBConnectionLabel),
            Width = Dim.Fill(),
        };

        var skipFileValidationLabel = new Label()
        {
            Text = "Skip file _validation before starting:",
            X = Pos.Left(tileDBConnectionLabel),
            Y = Pos.Bottom(tileDBConnectionLabel) + 1,
        };

        var skipFileValidationInput = new CheckBox()
        {
            Text = settings.SkipFileChecks switch
            {
                true => "Yes",
                false => "No",
                _ => "",
            },
            CheckedState = settings.SkipFileChecks switch
            {
                true => CheckState.Checked,
                false => CheckState.UnChecked,
                _ => CheckState.None,
            },
            X = Pos.Right(skipFileValidationLabel) + 1,
            Y = Pos.Y(skipFileValidationLabel),
        };
        skipFileValidationInput.CheckedStateChanged += (s, e) =>
        {
            skipFileValidationInput.Text = e.Value switch
            {
                CheckState.Checked => "Yes",
                CheckState.UnChecked => "No",
                _ => "",
            };
        };

        var cancelBtn = new Button()
        {
            Text = "_Cancel",
            X = Pos.Align(Alignment.Center, AlignmentModes.AddSpaceBetweenItems, ChoiceButtonsGroup),
            Y = Pos.AnchorEnd(),
        };
        cancelBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            Application.RequestStop();
        };

        var applyBtn = new Button()
        {
            Text = "_Apply",
            X = Pos.Align(Alignment.Center, AlignmentModes.AddSpaceBetweenItems, ChoiceButtonsGroup),
            Y = Pos.AnchorEnd(),
        };
        applyBtn.Accepting += (s, e) =>
        {
            e.Handled = true;

            if (!ushort.TryParse(apiPortInput.Text, out ushort apiPort))
            {
                MessageBox.ErrorQuery("Error", $"Api port is invalid, must be integer between 0 and {ushort.MaxValue}", "Ok");
            }
            else if (!ushort.TryParse(eventBusPortInput.Text, out ushort eventBusPort))
            {
                MessageBox.ErrorQuery("Error", $"EventBus port is invalid, must be integer between 0 and {ushort.MaxValue}", "Ok");
            }
            else if (!ushort.TryParse(objectStorePortInput.Text, out ushort objectStorePort))
            {
                MessageBox.ErrorQuery("Error", $"ObjectStore port is invalid, must be integer between 0 and {ushort.MaxValue}", "Ok");
            }
            else if (!IPAddress.TryParse(thisIPInput.Text, out var thisIP) || thisIP.AddressFamily is not System.Net.Sockets.AddressFamily.InterNetwork)
            {
                MessageBox.ErrorQuery("Error", $"IPv4 is invalid, must be a valid IPv4 address", "Ok");
            }
            else
            {
                settings.ApiPort = apiPort;
                settings.EventBusPort = eventBusPort;
                settings.ObjectStorePort = objectStorePort;
                settings.IPv4 = thisIP.ToString();
                settings.EarthDatabaseConnectionString = earthDBConnectionInput.Text;
                settings.TileDatabaseConnectionString = tileDBConnectionInput.Text;
                settings.SkipFileChecks = skipFileValidationInput.CheckedState switch
                {
                    CheckState.None => false,
                    CheckState.Checked => true,
                    CheckState.UnChecked => false,
                    _ => false,
                };

                Application.RequestStop();
            }
        };

        Add(apiPortLabel, apiPortInput, eventBusPortLabel, eventBusPortInput, objectStorePortLabel, objectStorePortInput, thisIPLabel, thisIPInput, earthDBConnectionLabel, earthDBConnectionInput, tileDBConnectionLabel, tileDBConnectionInput, skipFileValidationLabel, skipFileValidationInput, cancelBtn, applyBtn);
    }
}
