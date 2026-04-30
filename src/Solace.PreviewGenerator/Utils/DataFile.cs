using Serilog;
using System.Text.Json.Nodes;
using ViennaDotNet.Common;

namespace ViennaDotNet.PreviewGenerator.Utils;

public static class DataFile
{
    public static void Load(string path, Action<JsonNode> consumer)
    {
        try
        {
            consumer(Json.Deserialize<JsonNode>(File.ReadAllText(path))!);
        }
        catch (Exception ex)
        {
            Log.Fatal($"Cannot read resource '{path}': {ex}");
            Log.CloseAndFlush();
        }
    }
}
