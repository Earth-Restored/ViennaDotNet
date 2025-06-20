using Serilog;
using System.Diagnostics;
using System.Globalization;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;

namespace ViennaDotNet.Launcher;

internal static class Program
{
    public const string SettingsFile = "config.json";
    public const string ProgramsDir = "./"; // same as launcher
    public const string StaticDataDir = "staticdata";

    public static LoggerConfiguration LoggerConfiguration => new LoggerConfiguration()
            .WriteTo.Conditional(e => LogToConsole, wt => wt.Console())
            .WriteTo.File("logs/launcher/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .MinimumLevel.Debug();

    public static Settings Settings = Settings.Default;

    public static bool LogToConsole = true;

    static async Task Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        var log = LoggerConfiguration.CreateLogger();

        Log.Logger = log;

        if (!Debugger.IsAttached)
        {
            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                Log.Fatal($"Unhandeled exception: {e.ExceptionObject}");
                Log.CloseAndFlush();
                Environment.Exit(1);
            };
        }

        await AutoUpdater.CheckAndUpdate();

        Settings = await Settings.LoadAsync(SettingsFile);

        LogToConsole = false;

        Application.Run<LauncherWindow>().Dispose();

        Application.Shutdown();
    }

    //static void start()
    //{
    //    ConsoleE.ColorClear();
    //    Console.CursorVisible = true;

    //    // check "public" ip is valid
    //    if (!IPAddress.TryParse(Settings.IPv4, out var _))
    //    {
    //        Log.Information($"IP is invalid, go to Configure and set IPv4 to IP of this computer");
    //        U.PAK();
    //        Console.CursorVisible = false;
    //        return;
    //    }

    //    if (Settings.SkipFileChecks ?? false)
    //        Log.Warning("Skipped file validation, you can turn this on in 'Configure/Skip file validation before starting'");
    //    else
    //    {
    //        Log.Information("Checking files...");

    //        if (
    //            !ApiServer.Check() ||
    //            !Programs.Buildplate.Check() ||
    //            !EventBusServer.Check() ||
    //            !ObjectStoreServer.Check() ||
    //            !TappablesGenerator.Check()
    //        )
    //        {
    //            U.PAK();
    //            Console.CursorVisible = false;
    //            return;
    //        }

    //        Log.Debug("Vienna files checked");

    //        Java.Check();

    //        if (
    //            !MCServer.Check() ||
    //            !ConnectorPlugin.Check() ||
    //            !Fountain.Check()
    //        )
    //        {
    //            U.PAK();
    //            Console.CursorVisible = false;
    //            return;
    //        }

    //        Log.Information("Files validated");
    //    }

    //    Log.Information("Starting...");

    //    EventBusServer.Run(Settings);
    //    ObjectStoreServer.Run(Settings);
    //    ApiServer.Run(Settings);
    //    Programs.Buildplate.Run(Settings, $"./../{Fountain.DirName}/{Fountain.JarName}", $"./../{MCServer.DirName}", MCServer.ServerJarName, $"./../{ConnectorPlugin.JarName}");
    //    TappablesGenerator.Run(Settings);

    //    Log.Information("Started");
    //    Console.WriteLine("Make sure all *5* windows are open (not counting this one)");
    //    U.PAK();
    //    Console.CursorVisible = false;
    //}

    //static void importWorld()
    //{
    //    ConsoleE.ColorClear();
    //    Console.CursorVisible = true;

    //    if (!BuildplateImporter.Check())
    //    {
    //        U.PAK();
    //        Console.CursorVisible = false;
    //        return;
    //    }

    //    Console.WriteLine("ID of the player the buildplate will be added to: ");
    //    string playerId = ConsoleE.ReadNonWhiteSpaceLine();

    //    Console.WriteLine("Path to the !Java! world to import (directory or a zip file)");
    //    string worldPath;
    //    while (true)
    //    {
    //        worldPath = ConsoleE.ReadNonWhiteSpaceLine();
    //        if (File.Exists(worldPath) || Directory.Exists(worldPath))
    //            break;
    //        else
    //            Console.WriteLine("File/Directory doesn't exist");
    //    }

    //    int? exitCode = BuildplateImporter.Run(Settings, playerId, worldPath);
    //    if (exitCode is null)
    //        Log.Error("Failed to start importer");
    //    else if (exitCode != 0)
    //    {
    //        Log.Error($"Failed to import buildplate, error code: {exitCode}");
    //        Console.WriteLine($"Make sure {Programs.Buildplate.DispName}, {EventBusServer.DispName} and {ObjectStoreServer.DispName} are running");
    //    }
    //    else
    //        Log.Information("Imported buildplate");

    //    U.PAK();
    //    Console.CursorVisible = false;
    //}

    //static void modifyData()
    //{
    //    UIList uIList = new UIList(
    //    [
    //        new UIText("***ViennaDotNet Launcher/Modify data***"),
    //        new UISpacer(new Vector2I(0, 1)),
    //        new UIButton("Import", Data.Import),
    //        new UIButton("Export", () => Data.Export(Settings)),
    //        new UIButton("Delete", () => Data.Delete(Settings)),
    //        new UISpacer(new Vector2I(0, 1)),
    //        new UIButton("Back")
    //        {
    //            OnClickFunc = () => UIManager.ContinueOptions.CloseUI
    //        }
    //    ])
    //    {
    //        HorizontalOffset = 1
    //    };
    //    uIList.SetColor(ConsoleColor.White, ConsoleColor.Black);

    //    UIManager ui = new UIManager(uIList);

    //    ui.Open();
    //}

    //static void about()
    //{

    //}
}
