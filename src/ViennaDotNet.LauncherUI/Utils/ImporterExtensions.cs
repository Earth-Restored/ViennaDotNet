using System.Diagnostics;
using Serilog;
using ViennaDotNet.Buildplate.Model;
using ViennaDotNet.BuildplateImporter;
using ViennaDotNet.BuildplateRenderer;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Global;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.LauncherUI.Utils;

public static class ImporterExtensions
{
    extension(Importer)
    {
        public static async Task<Importer> CreateFromSettings(Settings settings, Serilog.ILogger logger, bool createEventBus = true)
        {
            var earthDB = EarthDB.Open(settings.EarthDatabaseConnectionString ?? "");
            var eventBus = createEventBus ? await EventBusClient.ConnectAsync($"localhost:{settings.EventBusPort}") : null;
            var objectStore = await ObjectStoreClient.ConnectAsync($"localhost:{settings.ObjectStorePort}");

            return new Importer(earthDB, eventBus, objectStore, logger);
        }
    }

    extension(Importer importer)
    {
        public async Task<ArraySegment<byte>?> GetTemplateLauncherPreviewAsync(string templateId, ResourcePackManager resourcePackManager, bool getFromCache = true, CancellationToken cancellationToken = default)
        {
            TemplateBuildplate? template;
            try
            {
                var results = await new EarthDB.ObjectQuery(false)
                   .GetBuildplate(templateId)
                   .ExecuteAsync(importer.EarthDB, cancellationToken);

                template = results.GetBuildplate(templateId);
            }
            catch (EarthDB.DatabaseException ex)
            {
                importer.Logger.Error($"Failed to fetch template {templateId}: {ex}");
                return null;
            }

            if (template is null)
            {
                importer.Logger.Warning($"Template {templateId} does not exist");
                return null;
            }

            if (!string.IsNullOrEmpty(template.LauncherPreviewObjectId))
            {
                if (getFromCache)
                {
                    var previewData = await importer.ObjectStoreClient.GetAsync(template.LauncherPreviewObjectId);

                    if (previewData is null)
                    {
                        importer.Logger.Error($"Could not get launcher preview for template '{templateId}'");
                        return null;
                    }

                    return previewData;
                }
                else
                {
                    await importer.ObjectStoreClient.DeleteAsync(template.LauncherPreviewObjectId);
                }
            }

            var worldDataRaw = await importer.ObjectStoreClient.GetAsync(template.ServerDataObjectId);

            if (worldDataRaw is null)
            {
                importer.Logger.Error($"Could not get world data for template '{templateId}'");
                return null;
            }

            WorldData? worldData;
            using (var worldDataStream = new MemoryStream(worldDataRaw))
            {
                worldData = await importer.ReadWorldFile(worldDataStream, cancellationToken);
            }

            if (worldData is null)
            {
                return null;
            }

            var meshGenerator = new BuildplateMeshGenerator(resourcePackManager);

            MeshData? meshData = await meshGenerator.GenerateAsync(worldData);
            if (meshData is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            await meshData.ToGlbAsync(resourcePackManager, ms);
            bool getBufferSuccess = ms.TryGetBuffer(out var buffer);
            Debug.Assert(getBufferSuccess);

            var launcherPreviewObjectId = await importer.ObjectStoreClient.StoreAsync(buffer);
            if (launcherPreviewObjectId is null)
            {
                Log.Warning($"Failed to store launcher preview for template '{templateId}'");
                return buffer;
            }

            template = template with { LauncherPreviewObjectId = launcherPreviewObjectId, };

            await new EarthDB.ObjectQuery(true)
                .UpdateBuildplate(templateId, template)
                .ExecuteAsync(importer.EarthDB, cancellationToken);

            return buffer;
        }

        public async Task<ArraySegment<byte>?> GetPlayerBuildplateLauncherPreviewAsync(string playerId, string buildplateId, ResourcePackManager resourcePackManager, bool getFromCache = true, CancellationToken cancellationToken = default)
        {
            Buildplates playerBuildplates;

            try
            {
                playerBuildplates = (await new EarthDB.Query(false)
                    .Get("buildplates", playerId, typeof(Buildplates))
                    .ExecuteAsync(importer.EarthDB, cancellationToken))
                    .Get<Buildplates>("buildplates");

            }
            catch (EarthDB.DatabaseException ex)
            {
                importer.Logger.Error($"Failed to remove buildplate '{buildplateId}' from database for player '{playerId}': {ex}");
                return null;
            }

            var buildplate = playerBuildplates.GetBuildplate(buildplateId);

            if (buildplate is null)
            {
                importer.Logger.Warning($"Player buildplate {buildplateId} does not exist");
                return null;
            }

            if (!string.IsNullOrEmpty(buildplate.LauncherPreviewObjectId))
            {
                if (getFromCache)
                {
                    var previewData = await importer.ObjectStoreClient.GetAsync(buildplate.LauncherPreviewObjectId);

                    if (previewData is null)
                    {
                        importer.Logger.Error($"Could not get launcher preview for buildplate '{buildplate}'");
                        return null;
                    }

                    return previewData;
                }
                else
                {
                    await importer.ObjectStoreClient.DeleteAsync(buildplate.LauncherPreviewObjectId);
                }
            }

            var worldDataRaw = await importer.ObjectStoreClient.GetAsync(buildplate.ServerDataObjectId);

            if (worldDataRaw is null)
            {
                importer.Logger.Error($"Could not get world data for buildplate '{buildplate}'");
                return null;
            }

            WorldData? worldData;
            using (var worldDataStream = new MemoryStream(worldDataRaw))
            {
                worldData = await importer.ReadWorldFile(worldDataStream, cancellationToken);
            }

            if (worldData is null)
            {
                return null;
            }

            var meshGenerator = new BuildplateMeshGenerator(resourcePackManager);

            MeshData? meshData = await meshGenerator.GenerateAsync(worldData);
            if (meshData is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            await meshData.ToGlbAsync(resourcePackManager, ms);
            bool getBufferSuccess = ms.TryGetBuffer(out var buffer);
            Debug.Assert(getBufferSuccess);

            var launcherPreviewObjectId = await importer.ObjectStoreClient.StoreAsync(buffer);
            if (launcherPreviewObjectId is null)
            {
                Log.Warning($"Failed to store launcher preview for buildplate '{buildplateId}'");
                return buffer;
            }

            buildplate = buildplate with { LauncherPreviewObjectId = launcherPreviewObjectId, };

            playerBuildplates.RemoveBuildplate(buildplateId);
            playerBuildplates.AddBuildplate(buildplateId, buildplate);

            await new EarthDB.Query(true)
                .Update("buildplates", playerId, playerBuildplates)
                .ExecuteAsync(importer.EarthDB, cancellationToken);

            return buffer;
        }
    }
}