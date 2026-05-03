using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Solace.Buildplate.Model;
using Solace.BuildplateImporter;
using Solace.BuildplateRenderer;
using Solace.DB;
using Solace.DB.Models.Global;
using Solace.DB.Models.Player;
using Solace.EventBus.Client;
using Solace.LauncherUI.Data;
using Solace.LauncherUI.Models.Db;
using Solace.ObjectStore.Client;

namespace Solace.LauncherUI.Utils;

#pragma warning disable CA1708 // Identifiers should differ by more than case
public static class ImporterExtensions
#pragma warning restore CA1708 // Identifiers should differ by more than case
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
        public async Task<ArraySegment<byte>?> GetTemplateLauncherPreviewAsync(string templateId, ApplicationDbContext appDbContext, ResourcePackManager resourcePackManager, bool getFromCache = true, CancellationToken cancellationToken = default)
        {
            var dbBuildplatePreview = await appDbContext.BuildplatePreviews
                .AsNoTracking()
                .FirstOrDefaultAsync(preview => preview.PlayerId == null && preview.BuildplateId == templateId, cancellationToken: cancellationToken);

            if (dbBuildplatePreview is not null)
            {
                if (getFromCache)
                {
                    return dbBuildplatePreview.PreviewData;
                }
                else
                {
                    appDbContext.BuildplatePreviews.Remove(dbBuildplatePreview);
                    await appDbContext.SaveChangesAsync(cancellationToken);
                }
            }

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

            var worldDataRaw = await importer.ObjectStoreClient.GetAsync(template.ServerDataObjectId);

            if (worldDataRaw is null)
            {
                importer.Logger.Error($"Could not get world data for template '{templateId}'");
                return null;
            }

            WorldData? worldData;
            using (var worldDataStream = new MemoryStream(worldDataRaw))
            {
                worldData = await WorldData.LoadFromZipAsync(worldDataStream, importer.Logger, cancellationToken);
            }

            if (worldData is null)
            {
                return null;
            }

            worldData = worldData with { Size = template.Size, Offset = template.Offset, Night = template.Night, };

            var meshGenerator = new BuildplateMeshGenerator(resourcePackManager);

            MeshData? meshData = await meshGenerator.GenerateAsync(worldData, cancellationToken);
            if (meshData is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            await meshData.ToGlbAsync(resourcePackManager, ms);
            bool getBufferSuccess = ms.TryGetBuffer(out var buffer);
            Debug.Assert(getBufferSuccess);

            dbBuildplatePreview = new DbBuildplatePreview()
            {
                PlayerId = null,
                BuildplateId = templateId,
                PreviewData = [.. buffer],
            };

            appDbContext.BuildplatePreviews.Add(dbBuildplatePreview);
            await appDbContext.SaveChangesAsync(cancellationToken);

            return buffer;
        }

        public async Task<ArraySegment<byte>?> GetPlayerBuildplateLauncherPreviewAsync(string playerId, string buildplateId, ApplicationDbContext appDbContext, ResourcePackManager resourcePackManager, bool getFromCache = true, CancellationToken cancellationToken = default)
        {
            var dbBuildplatePreview = await appDbContext.BuildplatePreviews
                .AsNoTracking()
                .FirstOrDefaultAsync(preview => preview.PlayerId == playerId && preview.BuildplateId == buildplateId, cancellationToken: cancellationToken);

            if (dbBuildplatePreview is not null)
            {
                if (getFromCache)
                {
                    return dbBuildplatePreview.PreviewData;
                }
                else
                {
                    appDbContext.BuildplatePreviews.Remove(dbBuildplatePreview);
                    await appDbContext.SaveChangesAsync(cancellationToken);
                }
            }

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

            var worldDataRaw = await importer.ObjectStoreClient.GetAsync(buildplate.ServerDataObjectId);

            if (worldDataRaw is null)
            {
                importer.Logger.Error($"Could not get world data for buildplate '{buildplate}'");
                return null;
            }

            WorldData? worldData;
            using (var worldDataStream = new MemoryStream(worldDataRaw))
            {
                worldData = await WorldData.LoadFromZipAsync(worldDataStream, importer.Logger, cancellationToken);
            }

            if (worldData is null)
            {
                return null;
            }

            worldData = worldData with { Size = buildplate.Size, Offset = buildplate.Offset, Night = buildplate.Night, };

            var meshGenerator = new BuildplateMeshGenerator(resourcePackManager);

            MeshData? meshData = await meshGenerator.GenerateAsync(worldData, cancellationToken);
            if (meshData is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            await meshData.ToGlbAsync(resourcePackManager, ms);
            bool getBufferSuccess = ms.TryGetBuffer(out var buffer);
            Debug.Assert(getBufferSuccess);

            dbBuildplatePreview = new DbBuildplatePreview()
            {
                PlayerId = playerId,
                BuildplateId = buildplateId,
                PreviewData = [.. buffer],
            };

            appDbContext.BuildplatePreviews.Add(dbBuildplatePreview);
            await appDbContext.SaveChangesAsync(cancellationToken);

            return buffer;
        }
    }
}