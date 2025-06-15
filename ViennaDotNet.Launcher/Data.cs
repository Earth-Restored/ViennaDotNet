using Serilog;
using System.IO.Compression;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.Launcher.Programs;

namespace ViennaDotNet.Launcher;

internal static class Data
{
    //public static void Import()
    //{
    //    ConsoleE.ColorClear();
    //    Log.Information("Importing data");

    //    if (Directory.Exists($"{ObjectStoreServer.DirName}/data") && ConsoleE.ReadBoolean("Do you want to delete the current data first?", ConsoleE.ReadBoolBehaviour.Retry))
    //        try
    //        {
    //            Directory.Delete($"{ObjectStoreServer.DirName}/data", true);
    //        }
    //        catch (Exception ex)
    //        {
    //            Log.Error($"Failed to delete current data: {ex}");
    //            U.PAK();
    //            return;
    //        }

    //    Console.WriteLine("Path to the exported data.zip");
    //    Console.CursorVisible = true;
    //    string path;
    //    while (!File.Exists(path = ConsoleE.ReadNonWhiteSpaceLine()))
    //        Console.WriteLine("File doesn't exist");
    //    Console.CursorVisible = false;

    //    try
    //    {
    //        using ZipArchive zip = ZipFile.OpenRead(path);

    //        foreach (var entry in zip.Entries)
    //            if (!entry.IsDirectory())
    //            {
    //                if (entry.FullName.StartsWith($"{ObjectStoreServer.DirName}/data") || entry.FullName.StartsWith($"{ObjectStoreServer.DirName}\\data"))
    //                {
    //                    Directory.CreateDirectory(Path.GetDirectoryName(entry.FullName)!);
    //                    using (Stream stream = entry.Open())
    //                    using (FileStream fs = File.OpenWrite(entry.FullName))
    //                        stream.CopyTo(fs);
    //                }
    //                else if (entry.Name == entry.FullName)
    //                { // top level file
    //                    using (Stream stream = entry.Open())
    //                    using (FileStream fs = File.OpenWrite(entry.FullName))
    //                        stream.CopyTo(fs);
    //                }
    //            }
    //    }
    //    catch (Exception ex)
    //    {
    //        Log.Error($"Error while importing data: {ex}");
    //        U.PAK();
    //        return;
    //    }

    //    Log.Information("Imported data");
    //    U.PAK();
    //}

    //public static void Export(Settings settings)
    //{
    //    ConsoleE.ColorClear();
    //    Log.Information("Exporting data");

    //    if (File.Exists("data.zip"))
    //    {
    //        Console.WriteLine("data.zip already exists, it will be deleted");
    //        U.PAK();
    //        File.Delete("data.zip");
    //    }

    //    using ZipArchive zip = ZipFile.Open("data.zip", ZipArchiveMode.Create);

    //    try
    //    {
    //        string databasePath = Path.Combine(ApiServer.DirName, settings.DatabaseConnectionString!);
    //        if (File.Exists(databasePath))
    //        {
    //            var entry = zip.CreateEntry(Path.GetFileName(databasePath));
    //            using (Stream stream = entry.Open())
    //                stream.Write(File.ReadAllBytes(databasePath));
    //        }

    //        foreach (string file in Directory.EnumerateFiles($"{ObjectStoreServer.DirName}/data", "*", SearchOption.AllDirectories))
    //        {
    //            var entry = zip.CreateEntry(Path.GetRelativePath(Environment.CurrentDirectory, file));
    //            using (Stream stream = entry.Open())
    //                stream.Write(File.ReadAllBytes(file));
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Log.Error($"Error while exporting data: {ex}");
    //        U.PAK();
    //        return;
    //    }

    //    Log.Information("Exported data to data.zip");
    //    U.PAK();
    //}

    //public static void Delete(Settings settings)
    //{
    //    ConsoleE.ColorClear();
    //    Log.Information("Deleting data");

    //    if (!ConsoleE.ReadBoolean("Are you sure you want to delete server data?", ConsoleE.ReadBoolBehaviour.False))
    //        return;

    //    try
    //    {
    //        string databasePath = Path.Combine(ApiServer.DirName, settings.DatabaseConnectionString!);
    //        if (File.Exists(databasePath))
    //            File.Delete(databasePath);

    //        Directory.Delete($"{ObjectStoreServer.DirName}/data", true);
    //    }
    //    catch (Exception ex)
    //    {
    //        Log.Error($"Error while deleting data: {ex}");
    //        U.PAK();
    //        return;
    //    }

    //    Log.Information("Data deleted");
    //    U.PAK();
    //}
}
