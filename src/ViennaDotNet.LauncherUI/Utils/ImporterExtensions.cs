using Serilog;
using ViennaDotNet.BuildplateImporter;
using ViennaDotNet.DB;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.LauncherUI.Utils;

public static class ImporterExtensions
{
    extension (Importer)
    {
        public static async Task<Importer> CreateFromSettings(Settings settings, Serilog.ILogger logger)
        {
            var earthDB = EarthDB.Open(settings.EarthDatabaseConnectionString ?? "");
            var eventBus = EventBusClient.Create($"localhost:{settings.EventBusPort}");
            var objectStore = ObjectStoreClient.Create($"localhost:{settings.ObjectStorePort}");
            
            return new Importer(earthDB, eventBus, objectStore, logger);
        }
    }
}