using Serilog;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ViennaDotNet.Buildplate.Model;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Global;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.BuildplateImporter;

public sealed class Importer : IAsyncDisposable
{
    public readonly EarthDB EarthDB;
    public readonly EventBusClient? EventBusClient;
    public readonly ObjectStoreClient ObjectStoreClient;
    public readonly ILogger Logger;

    public Importer(EarthDB earthDB, EventBusClient? eventBusClient, ObjectStoreClient objectStoreClient, ILogger logger)
    {
        EarthDB = earthDB;
        EventBusClient = eventBusClient;
        ObjectStoreClient = objectStoreClient;
        Logger = logger;
    }

    public async Task<bool> ImportTemplateAsync(string templateId, string name, Stream stream, CancellationToken cancellationToken = default)
    {
        var worldData = await WorldData.LoadFromZipAsync(stream, Logger, cancellationToken);

        if (worldData is null)
        {
            return false;
        }

        byte[] preview = await GeneratePreview(worldData);

        return await StoreTemplate(templateId, name, preview, worldData, cancellationToken);
    }

    public async Task<bool> RegenerateTemplatePreviewAsync(string templateId, CancellationToken cancellationToken = default)
    {
        TemplateBuildplate? template;
        try
        {
            var results = await new EarthDB.ObjectQuery(false)
               .GetBuildplate(templateId)
               .ExecuteAsync(EarthDB, cancellationToken);

            template = results.GetBuildplate(templateId);
        }
        catch (EarthDB.DatabaseException ex)
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
            worldData = await ReadWorldFile(ms, cancellationToken);
        }

        if (worldData is null)
        {
            return false;
        }

        byte[] preview = await GeneratePreview(worldData);

        string? newPreviewObjectId = await ObjectStoreClient.StoreAsync(preview);
        if (newPreviewObjectId is null)
        {
            Logger.Error($"Could not store template's preview object in object store '{templateId}'");
            return false;
        }

        var oldPreviewObjectId = template.PreviewObjectId;

        template = template with { PreviewObjectId = newPreviewObjectId, };

        try
        {
            var results = await new EarthDB.ObjectQuery(true)
               .UpdateBuildplate(templateId, template)
               .ExecuteAsync(EarthDB, cancellationToken);

            if (!string.IsNullOrEmpty(oldPreviewObjectId))
            {
                await ObjectStoreClient.DeleteAsync(oldPreviewObjectId);
                Logger.Debug($"Deleted old preview for template '{templateId}'");
            }

            return true;
        }
        catch (EarthDB.DatabaseException ex)
        {
            Logger.Error($"Failed to update template buidplate in database: {ex}");
            await ObjectStoreClient.DeleteAsync(newPreviewObjectId);
            return false;
        }
    }

    public async Task<bool> RemoveTemplateAsync(string templateId, bool removeFromPlayers, CancellationToken cancellationToken = default)
    {
        Logger.Information($"Starting removal of template {templateId}");

        TemplateBuildplate? template;
        try
        {
            var results = await new EarthDB.ObjectQuery(false)
               .GetBuildplate(templateId)
               .ExecuteAsync(EarthDB, cancellationToken);

            template = results.GetBuildplate(templateId);
        }
        catch (EarthDB.DatabaseException ex)
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
            var instances = new List<(string PlayerId, string BuildplateId)>();

            try
            {
                using var connection = EarthDB.OpenConnection(false);
                using var command = connection.CreateCommand();

                command.CommandText = """
                    SELECT objects.id, json_each.key 
                    FROM objects, json_each(objects.value, '$.buildplates')
                    WHERE objects.type = 'buildplates' 
                    AND json_extract(json_each.value, '$.templateId') = $templateId
                    """;

                var param = command.CreateParameter();
                param.ParameterName = "$templateId";
                param.Value = templateId;
                command.Parameters.Add(param);

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    instances.Add((reader.GetString(0), reader.GetString(1)));
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error scanning players for template {templateId}: {ex}");
                return false;
            }

            Logger.Information($"Found {instances.Count} player buildplates to remove.");

            foreach (var (playerId, buildplateId) in instances)
            {
                await RemoveBuildplateFromPlayer(buildplateId, playerId, cancellationToken);
            }
        }

        try
        {
            await new EarthDB.ObjectQuery(true)
                .UpdateBuildplate(templateId, null)
                .ExecuteAsync(EarthDB, cancellationToken);
        }
        catch (EarthDB.DatabaseException ex)
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

    public async Task<string?> AddBuidplateToPlayer(string templateId, string playerId, CancellationToken cancellationToken = default)
    {
        TemplateBuildplate? template;
        try
        {
            var results = await new EarthDB.ObjectQuery(false)
               .GetBuildplate(templateId)
               .ExecuteAsync(EarthDB, cancellationToken);

            template = results.GetBuildplate(templateId);
        }
        catch (EarthDB.DatabaseException ex)
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

        string buidplateId = U.RandomUuid().ToString();

        if (!await StoreBuildplate(templateId, playerId, buidplateId, template, serverData, preview, cancellationToken))
        {
            return null;
        }

        return buidplateId;
    }

    public async Task<bool> RegeneratePlayerBuildplatePreviewAsync(string playerId, string buildplateId, CancellationToken cancellationToken = default)
    {
        Buildplates playerBuildplates;

        try
        {
            playerBuildplates = (await new EarthDB.Query(true)
                .Get("buildplates", playerId, typeof(Buildplates))
                .ExecuteAsync(EarthDB, cancellationToken))
                .Get<Buildplates>("buildplates");

        }
        catch (EarthDB.DatabaseException ex)
        {
            Logger.Error($"Failed to remove buildplate '{buildplateId}' from database for player '{playerId}': {ex}");
            return false;
        }

        var buildplate = playerBuildplates.GetBuildplate(buildplateId);

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
            worldData = await ReadWorldFile(ms, cancellationToken);
        }

        if (worldData is null)
        {
            return false;
        }

        byte[] preview = await GeneratePreview(worldData);

        string? newPreviewObjectId = await ObjectStoreClient.StoreAsync(preview);
        if (newPreviewObjectId is null)
        {
            Logger.Error($"Could not store player buildplate's preview object in object store '{buildplateId}'");
            return false;
        }

        var oldPreviewObjectId = buildplate.PreviewObjectId;

        buildplate = buildplate with { PreviewObjectId = newPreviewObjectId, };

        playerBuildplates.AddBuildplate(buildplateId, buildplate);

        try
        {
            await new EarthDB.Query(true)
                .Update("buildplates", playerId, playerBuildplates)
                .ExecuteAsync(EarthDB);

            if (!string.IsNullOrEmpty(oldPreviewObjectId))
            {
                await ObjectStoreClient.DeleteAsync(oldPreviewObjectId);
                Logger.Debug($"Deleted old preview for player buildplate '{buildplateId}'");
            }

            return true;
        }
        catch (EarthDB.DatabaseException ex)
        {
            Logger.Error($"Failed to update player buildplates in database: {ex}");
            await ObjectStoreClient.DeleteAsync(newPreviewObjectId);
            return false;
        }
    }

    public async Task<bool> RemoveBuildplateFromPlayer(string buildplateId, string playerId, CancellationToken cancellationToken = default)
    {
        Logger.Information($"Removing buildplate {buildplateId} from player {playerId}");

        string? serverDataObjectId = null;
        string? previewObjectId = null;

        try
        {
            await new EarthDB.Query(true)
                .Get("buildplates", playerId, typeof(Buildplates))
                .Then(results =>
                {
                    Buildplates buildplates = results.Get<Buildplates>("buildplates");

                    var buildplate = buildplates.GetBuildplate(buildplateId);
                    if (buildplate == null)
                    {
                        Logger.Warning($"Buildplate {buildplateId} not found for player {playerId}. Nothing to remove.");
                        return null;
                    }

                    serverDataObjectId = buildplate.ServerDataObjectId;
                    previewObjectId = buildplate.PreviewObjectId;

                    buildplates.RemoveBuildplate(buildplateId);

                    return new EarthDB.Query(true)
                        .Update("buildplates", playerId, buildplates);
                })
                .ExecuteAsync(EarthDB, cancellationToken);

            if (!string.IsNullOrEmpty(serverDataObjectId))
            {
                Logger.Information($"Deleting server data object {serverDataObjectId}");
                await ObjectStoreClient.DeleteAsync(serverDataObjectId);
            }

            if (!string.IsNullOrEmpty(previewObjectId))
            {
                Logger.Information($"Deleting preview object {previewObjectId}");
                await ObjectStoreClient.DeleteAsync(previewObjectId);
            }

            return true;
        }
        catch (EarthDB.DatabaseException ex)
        {
            Logger.Error($"Failed to remove buildplate '{buildplateId}' from database for player '{playerId}': {ex}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"An unexpected error occurred while removing buildplate '{buildplateId}': {ex}");
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

    public async Task<WorldData?> ReadWorldFile(Stream stream, CancellationToken cancellationToken)
    {
        Dictionary<string, byte[]> worldFileContents = [];

        try
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.IsDirectory)
                    {
                        continue;
                    }

                    var entryPath = entry.FullName.AsSpan().Trim(['/', '\\']);

                    if (entryPath is not "buildplate_metadata.json")
                    {
                        // must be allocated here because of await
#pragma warning disable CA2014 // Do not use stackalloc in loops
                        Span<Range> parts = stackalloc Range[3];
#pragma warning restore CA2014 // Do not use stackalloc in loops
                        int partCount = entryPath.SplitAny(parts, ['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

                        if (partCount != 2)
                        {
                            continue;
                        }

                        if (entryPath[parts[0]] is not ("region" or "entities"))
                        {
                            continue;
                        }

                        if (entryPath[parts[1]] is not ("r.0.0.mca" or "r.0.-1.mca" or "r.-1.0.mca" or "r.-1.-1.mca"))
                        {
                            continue;
                        }
                    }

                    using (var entryStream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        await entryStream.CopyToAsync(ms, cancellationToken);

                        worldFileContents[entry.FullName] = ms.ToArray();
                    }
                }
            }
        }
        catch (IOException ex)
        {
            Logger.Error($"Could not read world file: {ex}");
            return null;
        }

        byte[] serverData;
        try
        {
            using (var zipStream = new MemoryStream())
            {
                using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    foreach (string dirName in (IEnumerable<string>)["region", "entities"])
                    {
                        foreach (string fileName in (IEnumerable<string>)["r.0.0.mca", "r.0.-1.mca", "r.-1.0.mca", "r.-1.-1.mca"])
                        {
                            string filePath = $"{dirName}/{fileName}";

                            if (!worldFileContents.TryGetValue(filePath, out byte[]? data))
                            {
                                Logger.Error($"World file is missing {filePath}");
                                return null;
                            }

                            var entry = zip.CreateEntry(filePath, CompressionLevel.SmallestSize);
                            using (var entryStream = entry.Open())
                            {
                                entryStream.Write(data);
                            }
                        }
                    }
                }

                serverData = zipStream.ToArray();
            }
        }
        catch (IOException ex)
        {
            Logger.Error($"Could not prepare server data: {ex}");
            return null;
        }

        int size;
        int offset;
        bool night;

        try
        {
            byte[]? buildplateMetadataFileData = worldFileContents.GetValueOrDefault("buildplate_metadata.json");
            string? buildplateMetadataString = buildplateMetadataFileData is not null
                ? Encoding.UTF8.GetString(buildplateMetadataFileData)
                : null;

            if (buildplateMetadataString is null)
            {
                Logger.Warning("World file does not contain buildplate_metadata.json, using default values");
                size = 16;
                offset = 63;
                night = false;
            }
            else
            {
                var buildplateMetadataVersion = Json.Deserialize<BuildplateMetadataVersion>(buildplateMetadataString);

                if (buildplateMetadataVersion is null)
                {
                    Logger.Error("Invalid buildplate metadata");
                    return null;
                }

                switch (buildplateMetadataVersion.Version)
                {
                    case 1:
                        {
                            var buildplateMetadata = Json.Deserialize<BuildplateMetadataV1>(buildplateMetadataString);

                            if (buildplateMetadata is null)
                            {
                                Logger.Error("Invalid buildplate metadata");
                                return null;
                            }

                            size = buildplateMetadata.Size;
                            offset = buildplateMetadata.Offset;
                            night = buildplateMetadata.Night;
                        }

                        break;
                    default:
                        {
                            Logger.Error($"Unsupported buildplate metadata version {buildplateMetadataVersion.Version}");
                            return null;
                        }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not read buildplate metadata file: {ex}");
            return null;
        }

        if (size != 8 && size != 16 && size != 32)
        {
            Logger.Error($"Invalid buildplate size {size}, must be on of: 8, 16, 32");
            return null;
        }

        return new WorldData(serverData, size, offset, night);
    }

    private async Task<byte[]> GeneratePreview(WorldData worldData)
    {
        string? preview;
        if (EventBusClient is not null)
        {
            Logger.Information("Generating preview");
            RequestSender requestSender = EventBusClient.AddRequestSender();
            preview = await requestSender.Request("buildplates", "preview", JsonSerializer.Serialize(new PreviewRequest(Convert.ToBase64String(worldData.ServerData), worldData.Night))).Task;
            requestSender.Close();

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

    private async Task<bool> StoreTemplate(string templateId, string name, byte[] preview, WorldData worldData, CancellationToken cancellationToken)
    {
        TemplateBuildplate? template;
        try
        {
            var results = await new EarthDB.ObjectQuery(false)
               .GetBuildplate(templateId)
               .ExecuteAsync(EarthDB, cancellationToken);

            template = results.GetBuildplate(templateId);
        }
        catch (EarthDB.DatabaseException ex)
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

            template = new TemplateBuildplate(name, worldData.Size, worldData.Offset, scale, worldData.Night, serverDataObjectId, previewObjectId, null);

            try
            {
                var results = await new EarthDB.ObjectQuery(true)
                   .UpdateBuildplate(templateId, template)
                   .ExecuteAsync(EarthDB, cancellationToken);
            }
            catch (EarthDB.DatabaseException ex)
            {
                Logger.Error($"Failed to store template buidplate in database: {ex}");
                await ObjectStoreClient.DeleteAsync(serverDataObjectId);
                await ObjectStoreClient.DeleteAsync(previewObjectId);
                return false;
            }
        }

        return true;
    }

    private async Task<bool> StoreBuildplate(string templateId, string playerId, string buildplateId, TemplateBuildplate template, byte[] serverData, byte[] preview, CancellationToken cancellationToken)
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
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("buildplates", playerId, typeof(Buildplates))
                .Then(results1 =>
                {
                    Buildplates buildplates = results1.Get<Buildplates>("buildplates");

                    long lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    var buildplate = new Buildplates.Buildplate(templateId, template.Name, template.Size, template.Offset, template.Scale, template.Night, lastModified, serverDataObjectId, previewObjectId, null);

                    buildplates.AddBuildplate(buildplateId, buildplate);

                    return new EarthDB.Query(true)
                        .Update("buildplates", playerId, buildplates);
                })
                .ExecuteAsync(EarthDB, cancellationToken);

            return true;
        }
        catch (EarthDB.DatabaseException ex)
        {
            Logger.Error($"Failed to store buildplate in database: {ex}");
            await ObjectStoreClient.DeleteAsync(serverDataObjectId);
            await ObjectStoreClient.DeleteAsync(previewObjectId);
            return false;
        }
    }
}