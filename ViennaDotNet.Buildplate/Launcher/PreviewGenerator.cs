using System.Buffers.Text;
using System.Diagnostics;
using System;
using ViennaDotNet.DB.Models.Player;
using Serilog;
using System.Text;
using Newtonsoft.Json;

namespace ViennaDotNet.Buildplate.Launcher
{
    public sealed class PreviewGenerator
    {
        private readonly string javaCmd;
	private readonly FileInfo fountainJar;

        public PreviewGenerator(string javaCmd, string fountainJar)
        {
            this.javaCmd = javaCmd;
            this.fountainJar = new FileInfo(fountainJar);
        }

        public string? generatePreview(byte[] serverData, bool isNight)
        {
            string previewString;
            try
            {
                using (MemoryStream ms = new MemoryStream(serverData))
                    previewString = ViennaDotNet.PreviewGenerator.Generator.Generate(ms);
            } catch (Exception exception)
            {
                Log.Error($"Error while generating buildplate preview: {exception}");
                return null;
            }

            Dictionary<string, object> previewObject;
            try
            {
                previewObject = JsonConvert.DeserializeObject<Dictionary<string, object>>(previewString)!;
            }
            catch (Exception exception)
            {
                Log.Error($"Error while processing buildplate preview generator response: {exception}");
                return null;
            }

            previewObject["isNight"] = isNight;

            string previewJson = JsonConvert.SerializeObject(previewObject);

            string previewBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(previewJson));

            return previewBase64;
        }
    }
}
