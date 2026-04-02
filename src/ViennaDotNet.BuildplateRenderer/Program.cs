using Serilog.Core;
using ViennaDotNet.Buildplate.Model;
using ViennaDotNet.BuildplateRenderer;

Console.WriteLine("Hello, World!");

var resourcePack = ResourcePack.Load(new DirectoryInfo("~/Downloads/minecraft".Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));

WorldData? worldData;

using (var fs = File.OpenRead("test.zip"))
{
    worldData = await WorldData.LoadFromZipAsync(fs, Logger.None);
}

if (worldData is null)
{
    Console.WriteLine("Failed to load world data.");
    return;
}

var meshGenerator = new MeshGenerator(resourcePack);

var mesh = await meshGenerator.GenerateAsync(worldData);

Console.WriteLine("Done");