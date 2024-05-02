using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.PreviewGenerator.Utils
{
    public static class DataFile
    {
        public static void Load(string path, Action<JToken> consumer)
        {
            try
            {
                consumer(JsonConvert.DeserializeObject<JToken>(File.ReadAllText(path))!);
            }
            catch (Exception ex)
            {
                Log.Fatal($"Cannot read resource '{path}': {ex}");
            }
        }
    }
}
