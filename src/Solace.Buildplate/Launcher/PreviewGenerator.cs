using Serilog;
using System.Text;
using ViennaDotNet.Common;
using ViennaDotNet.PreviewGenerator.Registry;

namespace ViennaDotNet.Buildplate.Launcher;

public static class PreviewGenerator
{
    public static string? GeneratePreview(byte[] serverData, bool isNight, string staticDataPath)
    {
        BedrockBlocks.Initialize(staticDataPath);
        JavaBlocks.Initialize(staticDataPath);

        string previewString;
        try
        {
            using (var ms = new MemoryStream(serverData))
            {
                previewString = ViennaDotNet.PreviewGenerator.Generator.Generate(ms);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error while generating buildplate preview: {ex}");
            return null;
        }

        Dictionary<string, object> previewObject;
        try
        {
            previewObject = Json.Deserialize<Dictionary<string, object>>(previewString)!;
        }
        catch (Exception ex)
        {
            Log.Error($"Error while processing buildplate preview generator response: {ex}");
            return null;
        }

        previewObject["isNight"] = isNight;

        string previewJson = Json.Serialize(previewObject);

        string previewBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(previewJson));

        Log.Information("Preview generated");
        return previewBase64;
    }
}
