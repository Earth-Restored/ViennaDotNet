using ConsoleUI;
using ConsoleUI.Elements;
using ConsoleUI.TextValidators;
using ConsoleUI.Utils;
using MathUtils.Vectors;
using Serilog;
using System.Net;
using ViennaDotNet.Launcher.Programs;

namespace ViennaDotNet.Launcher
{
    internal static class Program
    {
        public static Settings Settings = new Settings();
        private static readonly string settingsFile = "config.json";

        static void Main(string[] args)
        {
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .MinimumLevel.Debug()
                .CreateLogger();

            Log.Logger = log;

            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                Log.Fatal($"Unhandeled exception: {e.ExceptionObject}");
                Environment.Exit(1);
            };

            Settings = Settings.Load(settingsFile);

            UIList uIList = new UIList(new UIElement[]
            {
                new UIText("***ViennaDotNet Launcher***"),
                new UISpacer(new Vector2I(0, 1)),
                new UIButton("Start", start),
                new UIButton("Configure", configure),
                new UISpacer(new Vector2I(0, 1)),
                new UIButton("Import buildplate", importWorld),
                new UIButton("Modify data", modifyData),
                new UISpacer(new Vector2I(0, 1)),
                new UIButton("Exit")
                {
                    OnClickFunc = () => UIManager.ContinueOptions.CloseUI
                },
            })
            {
                HorizontalOffset = 1
            };
            uIList.SetColor(ConsoleColor.White, ConsoleColor.Black);

            UIManager ui = new UIManager(uIList);

            ui.Open();
        }

        static void start()
        {
            ConsoleE.ColorClear();
            Console.CursorVisible = true;

            // check "public" ip is valid
            if (!IPAddress.TryParse(Settings.IPv4, out var _))
            {
                Log.Information($"IP is invalid, go to Configure and set IPv4 to IP of this computer");
                U.PAK(true);
                Console.CursorVisible = false;
                return;
            }

            if (Settings.SkipFileChecks!.Value)
                Log.Warning("Skipped file validation, you can turn this off in 'Configure/Skip file validation before starting'");
            else
            {
                Log.Information("Checking files...");

                if (
                    !ApiServer.Check() ||
                    !Programs.Buildplate.Check() ||
                    !EventBusServer.Check() ||
                    !ObjectStoreServer.Check() ||
                    !TappablesGenerator.Check()
                )
                {
                    U.PAK(true);
                    Console.CursorVisible = false;
                    return;
                }

                Log.Debug("Vienna files checked");

                Java.Check();

                if (
                    !MCServer.Check() ||
                    !ConnectorPlugin.Check() ||
                    !Fountain.Check()
                )
                {
                    U.PAK(true);
                    Console.CursorVisible = false;
                    return;
                }

                Log.Information("Files validated");
            }
            Log.Information("Starting...");

            EventBusServer.Run(Settings);
            ObjectStoreServer.Run(Settings);
            ApiServer.Run(Settings);
            Programs.Buildplate.Run(Settings, $"./../{Fountain.DirName}/{Fountain.JarName}", $"./../{MCServer.DirName}", MCServer.ServerJarName, $"./../{ConnectorPlugin.JarName}");
            TappablesGenerator.Run(Settings);

            Log.Information("Started");
            Console.WriteLine("Make sure all *5* windows are open (not counting this one)");
            U.PAK();
            Console.CursorVisible = false;
        }

        static void configure()
        {
            UIList uIList = new UIList(new UIElement[]
            {
                new UIText("***ViennaDotNet Launcher/Configure***"),
                new UISpacer(new Vector2I(0, 1)),
                new UIInputField("Api port")
                {
                    Value = Settings.ApiPort!.ToString()!,
                    TextValidator = new ParsableTextValidator<ushort>($"Must be number between 0-{ushort.MaxValue}"),
                    OnTextEnter = text => {
                        Settings.ApiPort = ushort.Parse(text);
                        Settings.Save(settingsFile);
                    }
                },
                new UIInputField("EventBus port")
                {
                    Value = Settings.EventBusPort!.ToString()!,
                    TextValidator = new ParsableTextValidator<ushort>($"Must be number between 0-{ushort.MaxValue}"),
                    OnTextEnter = text => {
                        Settings.EventBusPort = ushort.Parse(text);
                        Settings.Save(settingsFile);
                    }
                },
                new UIInputField("ObjectStore port")
                {
                    Value = Settings.ObjectStorePort!.ToString()!,
                    TextValidator = new ParsableTextValidator<ushort>($"Must be number between 0-{ushort.MaxValue}"),
                    OnTextEnter = text => {
                        Settings.ObjectStorePort = ushort.Parse(text);
                        Settings.Save(settingsFile);
                    }
                },
                new UIInputField("IPv4 (IP of this computer)")
                {
                    Value = Settings.IPv4!,
                    TextValidator = new ParsableTextValidator<IPAddress>("Must be in IPv4 format (x.x.x.x)"),
                    OnTextEnter = text => {
                        Settings.IPv4 = text;
                        Settings.Save(settingsFile);
                    }
                },
                new UIInputField("Database connection string")
                {
                    Value = Settings.DatabaseConnectionString!,
                    OnTextEnter = text => {
                        Settings.DatabaseConnectionString = text;
                        Settings.Save(settingsFile);
                    }
                },
                new UIInputField("ObjectStore data directory")
                {
                    Value = Settings.ObjectStoreDataDir!,
                    OnTextEnter = text => {
                        Settings.ObjectStoreDataDir = text;
                        Settings.Save(settingsFile);
                    }
                },
                new UISpacer(new Vector2I(0, 1)),
                new UIBool("Skip file validation before starting")
                {
                    Value = Settings.SkipFileChecks!.Value,
                    OnInvoke = oldVal =>
                    {
                        Settings.SkipFileChecks = !oldVal;
                        Settings.Save(settingsFile);
                        return !oldVal;
                    }
                },
                new UISpacer(new Vector2I(0, 1)),
                new UIButton("Back")
                {
                    OnClickFunc = () => UIManager.ContinueOptions.CloseUI
                }
            })
            {
                HorizontalOffset = 1
            };
            uIList.SetColor(ConsoleColor.White, ConsoleColor.Black);

            UIManager ui = new UIManager(uIList);

            ui.Open();
        }

        static void importWorld()
        {
            ConsoleE.ColorClear();
            Console.CursorVisible = true;

            if (!BuildplateImporter.Check())
            {
                U.PAK(true);
                Console.CursorVisible = false;
                return;
            }

            Console.WriteLine("ID of the player the buildplate will be added to: ");
            string playerId = ConsoleE.ReadNonWhiteSpaceLine();

            Console.WriteLine("Path to the !Java! world to import (directory or a zip file)");
            string worldPath;
            while (true)
            {
                worldPath = ConsoleE.ReadNonWhiteSpaceLine();
                if (File.Exists(worldPath) || Directory.Exists(worldPath))
                    break;
                else
                    Console.WriteLine("File/Directory doesn't exist");
            }

            int? exitCode = BuildplateImporter.Run(Settings, playerId, worldPath);
            if (exitCode is null)
                Log.Error("Failed to start importer");
            else if (exitCode != 0)
            {
                Log.Error($"Failed to import buildplate, error code: {exitCode}");
                Console.WriteLine($"Make sure {Programs.Buildplate.DispName}, {EventBusServer.DispName} and {ObjectStoreServer.DispName} are running");
            }
            else
                Log.Information("Imported buildplate");

            U.PAK();
            Console.CursorVisible = false;
        }

        static void modifyData()
        {
            UIList uIList = new UIList(new UIElement[]
            {
                new UIText("***ViennaDotNet Launcher/Modify data***"),
                new UISpacer(new Vector2I(0, 1)),
                new UIButton("Import"),
                new UIButton("Export"),
                new UIButton("Delete"),
                new UISpacer(new Vector2I(0, 1)),
                new UIButton("Back")
                {
                    OnClickFunc = () => UIManager.ContinueOptions.CloseUI
                }
            })
            {
                HorizontalOffset = 1
            };
            uIList.SetColor(ConsoleColor.White, ConsoleColor.Black);

            UIManager ui = new UIManager(uIList);

            ui.Open();
        }

        static void about()
        {

        }
    }
}
