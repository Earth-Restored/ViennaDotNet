using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ViennaDotNet.Launcher.Programs;

internal static class ApiServer
{
    public const string DirName = "ApiServer";
    public static readonly string ExeName = "ApiServer" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DispName = "Api server";

    public static bool Check()
    {
        string exePath = Path.GetFullPath(Path.Combine(DirName, ExeName));
        if (!File.Exists(exePath))
        {
            Log.Error($"{DispName} exe doesn't exits: {exePath}");
            return false;
        }

        // check for some of the catalog files and dirs
        string[] catalogFiles =
        [
            "journalCatalog.json",
            "productCatalog.json",
            "recipes.json",
        ];
        foreach (string fileName in catalogFiles)
        {
            string path = Path.GetFullPath(Path.Combine(DirName, "data", "catalog", fileName));
            if (!File.Exists(path))
            {
                Log.Error($"Catalog file \"{Path.GetFileNameWithoutExtension(path)}\" doesn't exits: {path}");
                return false;
            }
        }

        string[] catalogDirs =
        [
            "efficiency_categories",
            "items",
        ];
        foreach (string dirName in catalogDirs)
        {
            string path = Path.GetFullPath(Path.Combine(ApiServer.DirName, "data", "catalog", dirName));
            if (!Directory.Exists(path))
            {
                Log.Error($"Directory \"{Path.GetFileName(path)}\" doesn't exits: {path}");
                return false;
            }
        }

        string resourcePack = Path.GetFullPath(Path.Combine(DirName, "data", "resourcepacks", "vanilla.zip"));
        if (!File.Exists(resourcePack))
        {
            Console.WriteLine($"Resourcepack wasn't found: {resourcePack}");
            Console.WriteLine("Download it from https://cdn.mceserv.net/availableresourcepack/resourcepacks/dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35 (using internet archive)");
            Console.WriteLine($"Rename it to vanilla.zip and move it to: {DirName}/data/resourcepacks");
            U.ConfirmType("done");

            if (!File.Exists(resourcePack))
            {
                Log.Error($"Resourcepack doesn't exist: {resourcePack}");
                return false;
            }
        }

        return true;
    }

    public static void Run(Settings settings)
    {
        Log.Information($"Running {DispName}");
        Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(DirName, ExeName)),
        [
            $"--port={settings.ApiPort}",
            $"--db={settings.EarthDatabaseConnectionString}",
            $"--eventbus=localhost:{settings.EventBusPort}",
            $"--objectstore=localhost:{settings.ObjectStorePort}"
        ])
        {
            WorkingDirectory = Path.Combine(Environment.CurrentDirectory, DirName),
            CreateNoWindow = false,
            UseShellExecute = true
        });
    }
}
