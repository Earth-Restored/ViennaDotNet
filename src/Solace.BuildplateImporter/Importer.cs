using Serilog;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Solace.Buildplate.Model;
using Solace.Common;
using Solace.Common.Utils;
using Solace.DB;
using Solace.DB.Models.Global;
using Solace.DB.Models.Player;
using Solace.EventBus.Client;
using Solace.ObjectStore.Client;
using Microsoft.EntityFrameworkCore;

namespace Solace.BuildplateImporter;

public sealed class Importer : IAsyncDisposable
{
    public readonly EarthDbContext EarthDB;
    public readonly EventBusClient? EventBusClient;
    public readonly ObjectStoreClient ObjectStoreClient;
    public readonly ILogger Logger;

    public Importer(EarthDbContext earthDB, EventBusClient? eventBusClient, ObjectStoreClient objectStoreClient, ILogger logger)
    {
        EarthDB = earthDB;
        EventBusClient = eventBusClient;
        ObjectStoreClient = objectStoreClient;
        Logger = logger;
    }

    public async Task<bool> ImportTemplateAsync(Guid templateId, string name, Stream stream, CancellationToken cancellationToken = default)
    {
        var worldData = await WorldData.LoadFromZipAsync(stream, Logger, cancellationToken);

        if (worldData is null)
        {
            return false;
        }

        byte[] preview = await GeneratePreview(worldData);

        return await StoreTemplate(templateId, name, preview, worldData, cancellationToken);
    }

    public async Task<bool> RegenerateTemplatePreviewAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        TemplateBuildplateEF? template;
        try
        {
            template = await EarthDB.TemplateBuildplates
                .AsTracking()
                .FirstOrDefaultAsync(template => template.Id == templateId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to fetch template {templateId}: {ex}");
            return false;
        }

        if (template is null)
        {
            Logger.Warning($"Template {templateId} does not exist");
            return false;
        }

        if (string.IsNullOrEmpty(template.ServerDataObjectId))
        {
            Logger.Error($"Template '{templateId}' has no associated world data");
            return false;
        }

        var serverData = await ObjectStoreClient.GetAsync(template.ServerDataObjectId);

        if (serverData is null)
        {
            Logger.Error($"Could not get world data for template '{templateId}'");
            return false;
        }

        WorldData? worldData;
        using (var ms = new MemoryStream(serverData))
        {
            worldData = await WorldData.LoadFromZipAsync(ms, Logger, cancellationToken);
        }

        if (worldData is null)
        {
            return false;
        }

        worldData = worldData with { Size = template.Size, Offset = template.Offset, Night = template.Night, };

        byte[] preview = await GeneratePreview(worldData);

        string? newPreviewObjectId = await ObjectStoreClient.StoreAsync(preview);
        if (newPreviewObjectId is null)
        {
            Logger.Error($"Could not store template's preview object in object store '{templateId}'");
            return false;
        }

        var oldPreviewObjectId = template.PreviewObjectId;

        template.PreviewObjectId = newPreviewObjectId;

        try
        {
            await EarthDB.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrEmpty(oldPreviewObjectId))
            {
                await ObjectStoreClient.DeleteAsync(oldPreviewObjectId);
                Logger.Debug($"Deleted old preview for template '{templateId}'");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to update template buidplate in database: {ex}");
            await ObjectStoreClient.DeleteAsync(newPreviewObjectId);
            return false;
        }
    }

    public async Task<bool> RemoveTemplateAsync(Guid templateId, bool removeFromPlayers, CancellationToken cancellationToken = default)
    {
        Logger.Information($"Starting removal of template {templateId}");

        TemplateBuildplateEF? template;
        try
        {
            template = await EarthDB.TemplateBuildplates
                .AsTracking()
                .FirstOrDefaultAsync(template => template.Id == templateId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to fetch template {templateId}: {ex}");
            return false;
        }

        if (template is null)
        {
            Logger.Warning($"Template {templateId} does not exist. Skipping.");
            return true;
        }

        if (removeFromPlayers)
        {
            List<BuildplateEF> instances;

            try
            {
                instances = await EarthDB.PlayerBuildplates
                     .AsNoTracking()
                     .Where(buildplate => buildplate.TemplateId == templateId)
                     .ToListAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error scanning players for template {templateId}: {ex}");
                return false;
            }

            Logger.Information($"Found {instances.Count} player buildplates to remove.");

            foreach (var buildplate in instances)
            {
                await RemoveBuildplateFromPlayer(buildplate.Id, buildplate.AccountId, cancellationToken);
            }
        }

        try
        {
            EarthDB.TemplateBuildplates.Remove(template);

            await EarthDB.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to remove template {templateId} from DB: {ex}");
            return false;
        }

        if (!string.IsNullOrEmpty(template.ServerDataObjectId))
        {
            await ObjectStoreClient.DeleteAsync(template.ServerDataObjectId);
        }

        if (!string.IsNullOrEmpty(template.PreviewObjectId))
        {
            await ObjectStoreClient.DeleteAsync(template.PreviewObjectId);
        }

        Logger.Information($"Successfully purged template {templateId} and all associated player buildplates.");
        return true;
    }

    public async Task<Guid?> AddBuidplateToPlayer(Guid templateId, Guid playerId, CancellationToken cancellationToken = default)
    {
        TemplateBuildplateEF? template;
        try
        {
            template = await EarthDB.TemplateBuildplates
                .AsNoTracking()
                .FirstOrDefaultAsync(template => template.Id == templateId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get template buildplate '{templateId}': {ex}");
            return null;
        }

        if (template is null)
        {
            Logger.Error($"Template buildplate {templateId} not found");
            return null;
        }

        byte[]? serverData = await ObjectStoreClient.GetAsync(template.ServerDataObjectId);

        if (serverData is null)
        {
            Logger.Error($"Could not get server data for template buildplate '{templateId}'");
            return null;
        }

        byte[]? preview = await ObjectStoreClient.GetAsync(template.PreviewObjectId);

        if (preview is null)
        {
            Logger.Warning($"Could not get preview for template buildplate {templateId}");
            preview = await GeneratePreview(new WorldData(serverData, template.Size, template.Offset, template.Night));
        }

        var buidplateId = Guid.CreateVersion7();

        if (!await StoreBuildplate(templateId, playerId, buidplateId, template, serverData, preview, cancellationToken))
        {
            return null;
        }

        return buidplateId;
    }

    public async Task<bool> RegeneratePlayerBuildplatePreviewAsync(Guid accountId, Guid buildplateId, CancellationToken cancellationToken = default)
    {
        BuildplateEF? buildplate;

        try
        {
            buildplate = await EarthDB.PlayerBuildplates
                .AsTracking()
                .FirstOrDefaultAsync(buildplate => buildplate.Id == buildplateId && buildplate.AccountId == accountId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to remove buildplate '{buildplateId}' from player '{accountId}': {ex.Message}");
            return false;
        }

        if (buildplate is null)
        {
            Logger.Warning($"Player buildplate {buildplateId} does not exist");
            return false;
        }

        if (string.IsNullOrEmpty(buildplate.ServerDataObjectId))
        {
            Logger.Error($"Player buildplate '{buildplateId}' has no associated world data");
            return false;
        }

        var serverData = await ObjectStoreClient.GetAsync(buildplate.ServerDataObjectId);

        if (serverData is null)
        {
            Logger.Error($"Could not get world data for player buildplate '{buildplateId}'");
            return false;
        }

        WorldData? worldData;
        using (var ms = new MemoryStream(serverData))
        {
            worldData = await WorldData.LoadFromZipAsync(ms, Logger, cancellationToken);
        }

        if (worldData is null)
        {
            return false;
        }

        worldData = worldData with { Size = buildplate.Size, Offset = buildplate.Offset, Night = buildplate.Night, };

        byte[] preview = await GeneratePreview(worldData);

        string? newPreviewObjectId = await ObjectStoreClient.StoreAsync(preview);
        if (newPreviewObjectId is null)
        {
            Logger.Error($"Could not store player buildplate's preview object in object store '{buildplateId}'");
            return false;
        }

        var oldPreviewObjectId = buildplate.PreviewObjectId;

        buildplate.PreviewObjectId = newPreviewObjectId;

        try
        {
            await EarthDB.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrEmpty(oldPreviewObjectId))
            {
                await ObjectStoreClient.DeleteAsync(oldPreviewObjectId);
                Logger.Debug($"Deleted old preview for player buildplate '{buildplateId}'");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to update player buildplates in database: {ex}");
            await ObjectStoreClient.DeleteAsync(newPreviewObjectId);
            return false;
        }
    }

    public async Task<bool> RemoveBuildplateFromPlayer(Guid buildplateId, Guid accountId, CancellationToken cancellationToken = default)
    {
        Logger.Information($"Removing buildplate {buildplateId} from player {accountId}");

        try
        {
            var buildplate = await EarthDB.PlayerBuildplates
                .AsTracking()
                .FirstOrDefaultAsync(buildplate => buildplate.Id == buildplateId && buildplate.AccountId == accountId, cancellationToken);

            if (buildplate is null)
            {
                Logger.Warning($"Buildplate {buildplateId} not found for player {accountId}. Nothing to remove.");
                return true;
            }

            EarthDB.PlayerBuildplates.Remove(buildplate);
            await EarthDB.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrEmpty(buildplate.ServerDataObjectId))
            {
                Logger.Information($"Deleting server data object {buildplate.ServerDataObjectId}");
                await ObjectStoreClient.DeleteAsync(buildplate.ServerDataObjectId);
            }

            if (!string.IsNullOrEmpty(buildplate.PreviewObjectId))
            {
                Logger.Information($"Deleting preview object {buildplate.PreviewObjectId}");
                await ObjectStoreClient.DeleteAsync(buildplate.PreviewObjectId);
            }

            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or DbUpdateConcurrencyException)
        {
            Logger.Error(ex, $"Failed to remove buildplate '{buildplateId}' from database for player '{accountId}': {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"An unexpected error occurred while removing buildplate '{buildplateId}': {ex.Message}");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        EarthDB.Dispose();
        if (EventBusClient is not null)
        {
            await EventBusClient.DisposeAsync();
        }

        await ObjectStoreClient.DisposeAsync();
    }

    private async Task<byte[]> GeneratePreview(WorldData worldData)
    {
        string? preview;
        if (EventBusClient is not null)
        {
            Logger.Information("Generating preview");
            RequestSender requestSender = await EventBusClient.AddRequestSenderAsync();
            preview = await requestSender.RequestAsync("buildplates", "preview", JsonSerializer.Serialize(new PreviewRequest(Convert.ToBase64String(worldData.ServerData), worldData.Night)));
            await requestSender.CloseAsync();

            if (preview is null)
            {
                Logger.Warning("Could not get preview for buildplate (preview generator did not respond to event bus request)");
            }
        }
        else
        {
            Logger.Information("Preview was not generated because event bus is not connected");
            preview = null;
        }

        return preview is not null ? Encoding.ASCII.GetBytes(preview) : [];
    }

    private async Task<bool> StoreTemplate(Guid templateId, string name, byte[] preview, WorldData worldData, CancellationToken cancellationToken)
    {
        TemplateBuildplateEF? template;
        try
        {
            template = await EarthDB.TemplateBuildplates
                .AsNoTracking()
                .FirstOrDefaultAsync(template => template.Id == templateId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get template buildplate: {ex}");
            return false;
        }

        if (template is not null)
        {
            Logger.Error("Template buidplate already exists");
            return false;
            /*_logger.Information("Template buildplate found, updating");

            _logger.Information("Storing template world");
            string? serverDataObjectId = (string?)await objectStoreClient.Store(worldData.ServerData).Task;
            if (serverDataObjectId is null)
            {
                _logger.Error("Could not store template data object in object store");
                return false;
            }

            _logger.Information("Storing template preview");
            string? previewObjectId = (string?)await objectStoreClient.Store(preview).Task;
            if (previewObjectId is null)
            {
                _logger.Error("Could not store template preview object in object store");
                return false;
            }

            _logger.Information("Updating template object ids");
            string oldDataObjectId = template.ServerDataObjectId;
            string oldPreviewObjectId = template.PreviewObjectId;

            template = template with
            {
                ServerDataObjectId = serverDataObjectId,
                PreviewObjectId = previewObjectId
            };

            try
            {
                var results = await new EarthDB.ObjectQuery(true)
                   .UpdateBuildplate(templateId, template)
                   .ExecuteAsync(earthDB, cancellationToken);
            }
            catch (EarthDB.DatabaseException ex)
            {
                _logger.Error($"Failed to update template buildplate: {ex}");
                return false;
            }

            _logger.Information("Deleting old template objects");
            await objectStoreClient.Delete(oldDataObjectId).Task;
            await objectStoreClient.Delete(oldPreviewObjectId).Task;*/
        }
        else
        {
            Logger.Information("Template buildplate not found");

            Logger.Information("Storing template world");
            string? serverDataObjectId = await ObjectStoreClient.StoreAsync(worldData.ServerData);
            if (serverDataObjectId is null)
            {
                Logger.Error("Could not store template data object in object store");
                return false;
            }

            Logger.Information("Storing template preview");
            string? previewObjectId = await ObjectStoreClient.StoreAsync(preview);
            if (previewObjectId is null)
            {
                Logger.Error("Could not store template preview object in object store");
                return false;
            }

            int scale = worldData.Size switch
            {
                8 => 14,
                16 => 33,
                32 => 64,
                _ => 33,
            };

            template = new TemplateBuildplateEF()
            {
                Id = templateId,
                Name = name,
                Size = worldData.Size,
                Offset = worldData.Offset,
                Scale = scale,
                Night = worldData.Night,
                ServerDataObjectId = serverDataObjectId,
                PreviewObjectId = previewObjectId,
            };

            try
            {
                EarthDB.TemplateBuildplates.Add(template);
                await EarthDB.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to store template buidplate in database: {ex}");
                await ObjectStoreClient.DeleteAsync(serverDataObjectId);
                await ObjectStoreClient.DeleteAsync(previewObjectId);
                return false;
            }
        }

        return true;
    }

    private async Task<bool> StoreBuildplate(Guid templateId, Guid accountId, Guid buildplateId, TemplateBuildplateEF template, byte[] serverData, byte[] preview, CancellationToken cancellationToken)
    {
        Logger.Information("Storing world");
        string? serverDataObjectId = await ObjectStoreClient.StoreAsync(serverData);
        if (serverDataObjectId is null)
        {
            Logger.Error("Could not store data object in object store");
            return false;
        }

        Logger.Information("Storing preview");
        string? previewObjectId = await ObjectStoreClient.StoreAsync(preview);
        if (previewObjectId is null)
        {
            Logger.Error("Could not store preview object in object store");
            await ObjectStoreClient.DeleteAsync(serverDataObjectId);
            return false;
        }

        try
        {
            long lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            EarthDB.PlayerBuildplates.Add(new BuildplateEF()
            {
                Id = buildplateId,
                AccountId = accountId,
                TemplateId = templateId,
                Name = template.Name,
                Size = template.Size,
                Offset = template.Offset,
                Scale = template.Scale,
                Night = template.Night,
                LastModified = lastModified,
                ServerDataObjectId = template.ServerDataObjectId,
                PreviewObjectId = template.PreviewObjectId,
            });

            await EarthDB.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to store buildplate in database: {ex}");
            await ObjectStoreClient.DeleteAsync(serverDataObjectId);
            await ObjectStoreClient.DeleteAsync(previewObjectId);
            return false;
        }
    }
}