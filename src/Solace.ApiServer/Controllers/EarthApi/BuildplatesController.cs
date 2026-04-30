using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Buildplates;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Types.Inventory;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Global;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.ObjectStore.Client;
using ViennaDotNet.StaticData;
using Buildplates = ViennaDotNet.DB.Models.Player.Buildplates;

namespace ViennaDotNet.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
public class BuildplatesController : ViennaControllerBase
{
    private static EarthDB earthDB => Program.DB;
    private static BuildplateInstancesManager buildplateInstancesManager => Program.buildplateInstancesManager;
    private static Catalog catalog => Program.staticData.Catalog;
    private static TappablesManager tappablesManager => Program.tappablesManager;

    [HttpGet("buildplates")]
    public async Task<Results<ContentHttpResult, BadRequest>> GetBuildplates(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        Buildplates buildplatesModel;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("buildplates", playerId, typeof(Buildplates))
                .ExecuteAsync(earthDB, cancellationToken);
            buildplatesModel = results.Get<Buildplates>("buildplates");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        await using var objectStoreClient = await Program.GetObjectStoreClient();

        OwnedBuildplate[] ownedBuildplates = await Task.WhenAll(buildplatesModel.GetBuildplates().Select(async buildplateEntry =>
        {
            byte[]? previewData = await objectStoreClient.GetAsync(buildplateEntry.Buildplate.PreviewObjectId);
            if (previewData is null)
            {
                Log.Error($"Preview object {buildplateEntry.Buildplate.PreviewObjectId} for buildplate {buildplateEntry.Id} could not be loaded from object store");
                return null!;
            }

            string model = Encoding.ASCII.GetString(previewData);
            return new OwnedBuildplate(
                buildplateEntry.Id,
                "00000000-0000-0000-0000-000000000000",
                new Dimension(buildplateEntry.Buildplate.Size, buildplateEntry.Buildplate.Size),
                new Offset(0, buildplateEntry.Buildplate.Offset, 0),
                buildplateEntry.Buildplate.Scale,
                OwnedBuildplate.TypeE.SURVIVAL,
                SurfaceOrientation.HORIZONTAL,
                model,
                0,    // TODO
                false,    // TODO
                0,    // TODO
                false,    // TODO
                TimeFormatter.FormatTime(buildplateEntry.Buildplate.LastModified),
                0,    // TODO
                ""
            );
        }).Where(ownedBuildplate => ownedBuildplate is not null));

        return EarthJson(ownedBuildplates);
    }

    [HttpPost("multiplayer/buildplate/{buildplateId}/instances")]
    public async Task<Results<ContentHttpResult, InternalServerError, NotFound, BadRequest>> CreateBuildInstance(string buildplateId, CancellationToken cancellationToken)
    {
        // TODO: coordinates etc.

        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(playerId)
            ? TypedResults.BadRequest()
            : await GetNewBuildplateInstanceResponse(playerId, buildplateId, BuildplateInstancesManager.InstanceType.BUILD, cancellationToken);
    }

    [HttpPost("multiplayer/buildplate/{buildplateId}/play/instances")]
    public async Task<Results<ContentHttpResult, InternalServerError, NotFound, BadRequest>> CreatePlayInstance(string buildplateId, CancellationToken cancellationToken)
    {
        // TODO: coordinates etc.

        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(playerId)
            ? TypedResults.BadRequest()
            : await GetNewBuildplateInstanceResponse(playerId, buildplateId, BuildplateInstancesManager.InstanceType.PLAY, cancellationToken);
    }

    [HttpPost("buildplates/{buildplateId}/share")]
    public async Task<Results<ContentHttpResult, BadRequest, NotFound, InternalServerError>> ShareBuildplate(string buildplateId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        long requestStartedOn = HttpContext.GetTimestamp();

        DB.Models.Player.Inventory inventory;
        Hotbar hotbar;
        Buildplates.Buildplate? buildplate;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("inventory", playerId, typeof(DB.Models.Player.Inventory))
                .Get("hotbar", playerId, typeof(Hotbar))
                .Get("buildplates", playerId, typeof(Buildplates))
                .ExecuteAsync(earthDB, cancellationToken);

            inventory = results.Get<DB.Models.Player.Inventory>("inventory");
            hotbar = results.Get<Hotbar>("hotbar");
            buildplate = results.Get<Buildplates>("buildplates").GetBuildplate(buildplateId);
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        if (buildplate is null)
        {
            return TypedResults.NotFound();
        }

        await using var objectStoreClient = await Program.GetObjectStoreClient();

        byte[]? serverData = await objectStoreClient.GetAsync(buildplate.ServerDataObjectId);
        if (serverData is null)
        {
            Log.Error($"Data object {buildplate.ServerDataObjectId} for buildplate {buildplateId} could not be loaded from object store");
            return TypedResults.InternalServerError();
        }

        string? sharedBuildplateServerDataObjectId = await objectStoreClient.StoreAsync(serverData);
        if (sharedBuildplateServerDataObjectId is null)
        {
            Log.Error("Could not store data object for shared buildplate in object store");
            return TypedResults.InternalServerError();
        }

        string sharedBuildplateId = U.RandomUuid().ToString();
        var sharedBuildplate = new SharedBuildplates.SharedBuildplate(
            playerId,
            buildplate.Size,
            buildplate.Offset,
            buildplate.Scale,
            buildplate.Night,
            requestStartedOn,
            buildplate.LastModified,
            sharedBuildplateServerDataObjectId
        );

        for (int index = 0; index < 7; index++)
        {
            Hotbar.Item? item = hotbar.Items[index];
            SharedBuildplates.SharedBuildplate.HotbarItem? sharedBuildplateHotbarItem;
            if (item is null)
            {
                sharedBuildplateHotbarItem = null;
            }
            else if (item.InstanceId is null)
            {
                sharedBuildplateHotbarItem = new SharedBuildplates.SharedBuildplate.HotbarItem(item.Uuid, item.Count, null, 0);
            }
            else
            {
                sharedBuildplateHotbarItem = new SharedBuildplates.SharedBuildplate.HotbarItem(item.Uuid, 1, item.InstanceId, inventory.GetItemInstance(item.Uuid, item.InstanceId)?.Wear ?? 0);
            }

            sharedBuildplate.Hotbar[index] = sharedBuildplateHotbarItem;
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("sharedBuildplates", "", typeof(SharedBuildplates))
                .Then(results1 =>
                {
                    SharedBuildplates sharedBuildplates = results1.Get<SharedBuildplates>("sharedBuildplates");

                    sharedBuildplates.AddSharedBuildplate(sharedBuildplateId, sharedBuildplate);

                    return new EarthDB.Query(true)
                        .Update("sharedBuildplates", "", sharedBuildplates);
                })
                .ExecuteAsync(earthDB, cancellationToken);
        }
        catch (EarthDB.DatabaseException exception)
        {
            await objectStoreClient.DeleteAsync(sharedBuildplateServerDataObjectId);
            throw new ServerErrorException(exception);
        }

        return EarthJson($"minecraftearth://sharedbuildplate?id={sharedBuildplateId}");
    }

    [HttpGet("buildplates/shared/{sharedBuildplateId}")]
    public async Task<Results<ContentHttpResult, BadRequest, NotFound, InternalServerError>> GetSharedBuildplate(string sharedBuildplateId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        SharedBuildplates.SharedBuildplate? sharedBuildplate;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                    .Get("sharedBuildplates", "", typeof(SharedBuildplates))
                        .ExecuteAsync(earthDB, cancellationToken);
            SharedBuildplates sharedBuildplates = results.Get<SharedBuildplates>("sharedBuildplates");
            sharedBuildplate = sharedBuildplates.GetSharedBuildplate(sharedBuildplateId);
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        if (sharedBuildplate is null)
        {
            return TypedResults.NotFound();
        }
        
        await using var objectStoreClient = await Program.GetObjectStoreClient();

        byte[]? serverData = await objectStoreClient.GetAsync(sharedBuildplate.ServerDataObjectId);
        if (serverData is null)
        {
            Log.Error($"Data object {sharedBuildplate.ServerDataObjectId} for shared buildplate {sharedBuildplateId} could not be loaded from object store");
            return TypedResults.InternalServerError();
        }

        string? preview = await buildplateInstancesManager.GetBuildplatePreviewAsync(serverData, sharedBuildplate.Night);
        if (preview is null)
        {
            Log.Error("Could not get preview for buildplate");
            return TypedResults.InternalServerError();
        }

        return EarthJson(new SharedBuildplate(
            sharedBuildplate.PlayerId,    // TODO: supposed to return username here, not player ID
            TimeFormatter.FormatTime(sharedBuildplate.Created),
            new SharedBuildplate.BuildplateDataR(
                new Dimension(sharedBuildplate.Size, sharedBuildplate.Size),
                new Offset(0, sharedBuildplate.Offset, 0),
                sharedBuildplate.Scale,
                SharedBuildplate.BuildplateDataR.TypeE.SURVIVAL,
                SurfaceOrientation.HORIZONTAL,
                preview,
                0
            ),
            new Types.Inventory.Inventory(
                [.. sharedBuildplate.Hotbar.Select(item => item is not null ? new HotbarItem(
                    item.Uuid,
                    item.Count,
                    item.InstanceId,
                    item.InstanceId is not null ? ItemWear.WearToHealth(item.Uuid, item.Wear, catalog.ItemsCatalog) : 0.0f
                ) : null)],
                [.. sharedBuildplate.Hotbar
                    .Where(item => item is not null && item.InstanceId is null)
                    .Select(item => item!.Uuid)
                    .Distinct()
                    .Select(uuid => new StackableInventoryItem(
                        uuid,
                        0,
                        1,
                        // TODO: what unlocked/last seen timestamp are we supposed to use here - the player who shared the buildplate or the player who is viewing the buildplate?
                        new StackableInventoryItem.OnR(TimeFormatter.FormatTime(0)),
                        new StackableInventoryItem.OnR(TimeFormatter.FormatTime(0))
                    ))],
                [.. sharedBuildplate.Hotbar
                    .Where(item => item is not null && item.InstanceId is not null)
                    .Select(item => item!.Uuid)
                    .Distinct()
                    .Select(uuid => new NonStackableInventoryItem(
                        uuid,
                        [],
                        1,
                        // TODO: what unlocked/last seen timestamp are we supposed to use here - the player who shared the buildplate or the player who is viewing the buildplate?
                        new NonStackableInventoryItem.OnR(TimeFormatter.FormatTime(0)),
                        new NonStackableInventoryItem.OnR(TimeFormatter.FormatTime(0))
                    ))]
            )
        ));
    }

    [HttpPost("multiplayer/buildplate/shared/{sharedBuildplateId}/play/instances")]
    public async Task<Results<ContentHttpResult, NotFound, BadRequest, InternalServerError>> GetSharedBuildplateInstance(string sharedBuildplateId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        // TODO: coordinates etc.

        SharedBuildplateInstanceRequest sharedBuildplateInstanceRequest = (await Request.Body.AsJsonAsync<SharedBuildplateInstanceRequest>(cancellationToken))!;

        return await GetNewSharedBuildplateInstanceResponse(playerId, sharedBuildplateId, sharedBuildplateInstanceRequest.FullSize ? BuildplateInstancesManager.InstanceType.SHARED_PLAY : BuildplateInstancesManager.InstanceType.SHARED_BUILD, cancellationToken);
    }

    private sealed record EncounterInstanceRequest(
        string TileId
    );

    [HttpPost("multiplayer/encounters/{encounterId}/instances")]
    public async Task<Results<ContentHttpResult, NotFound, BadRequest, InternalServerError>> CreateEncounterInstance(string encounterId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        var encounterInstanceRequest = await Request.Body.AsJsonAsync<EncounterInstanceRequest>(cancellationToken);

        return encounterInstanceRequest is null
            ? TypedResults.BadRequest()
            : await GetNewEncounterBuildplateInstanceResponse(encounterId, encounterInstanceRequest.TileId, tappablesManager, cancellationToken);
    }

    // TODO: should we restrict this to matching player ID?
    [HttpGet("multiplayer/partitions/{partitionId}/instances/{instanceId}")]
    public async Task<Results<ContentHttpResult, BadRequest, NotFound>> GetInstanceStatus(string partitionId, string instanceId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        BuildplateInstancesManager.InstanceInfo? instanceInfo = buildplateInstancesManager.GetInstanceInfo(instanceId);
        if (instanceInfo is null || instanceInfo.ShuttingDown)
        {
            return TypedResults.NotFound();
        }

        Buildplates.Buildplate? buildplate;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                    .Get("buildplates", playerId, typeof(Buildplates))
                    .ExecuteAsync(earthDB, cancellationToken);
            buildplate = results.Get<Buildplates>("buildplates").GetBuildplate(instanceInfo.BuildplateId);
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        if (buildplate is null)
        {
            return TypedResults.NotFound();
        }

        // TODO: the client is supposed to poll until the buildplate server is ready, but instead it just crashes if we tell it that the buildplate server is not ready yet
        // TODO: so instead we just stall the request until it's ready, this is really ugly and eventually we need to figure out why it's crashing and implement this properly
        // TODO: this also relies on the buildplate server starting in less than ~20 seconds as the client will eventually time out the HTTP request and crash anyway
        //BuildplateInstance buildplateInstance = this.instanceInfoToApiResponse(instanceInfo);
        BuildplateInstancesManager.InstanceInfo? instanceInfo1;
        int waitCount = 0;
        do
        {
            instanceInfo1 = buildplateInstancesManager.GetInstanceInfo(instanceId);
            if (instanceInfo1 is null || instanceInfo1.ShuttingDown)
            {
                return TypedResults.NotFound();
            }

            if (!instanceInfo1.Ready)
            {
                await Task.Delay(1000, cancellationToken);

                waitCount++;
            }
        }
        while (!instanceInfo1.Ready && waitCount < 35);
        BuildplateInstance? buildplateInstance = await InstanceInfoToApiResponse(instanceInfo1, cancellationToken);

        if (buildplateInstance is null)
        {
            return TypedResults.NotFound();
        }

        return EarthJson(buildplateInstance);
    }

    private async Task<Results<ContentHttpResult, InternalServerError, NotFound, BadRequest>> GetNewBuildplateInstanceResponse(string playerId, string buildplateId, BuildplateInstancesManager.InstanceType type, CancellationToken cancellationToken)
    {
        Buildplates.Buildplate? buildplate;
        
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("buildplates", playerId, typeof(Buildplates))
                .ExecuteAsync(earthDB, cancellationToken);

            buildplate = results.Get<Buildplates>("buildplates").GetBuildplate(buildplateId);
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        if (buildplate is null)
        {
            return TypedResults.NotFound();
        }

        string? instanceId = await buildplateInstancesManager.RequestBuildplateInstance(playerId, null, buildplateId, type, 0, buildplate.Night);
        if (instanceId is null)
        {
            return TypedResults.InternalServerError();
        }

        BuildplateInstancesManager.InstanceInfo? instanceInfo = buildplateInstancesManager.GetInstanceInfo(instanceId);
        if (instanceInfo is null)
        {
            return TypedResults.InternalServerError();
        }

        BuildplateInstance? buildplateInstance = await InstanceInfoToApiResponse(instanceInfo, cancellationToken);

        if (buildplateInstance is null)
        {
            return TypedResults.NotFound();
        }

        return EarthJson(buildplateInstance);
    }

    private async Task<Results<ContentHttpResult, NotFound, BadRequest, InternalServerError>> GetNewSharedBuildplateInstanceResponse(string playerId, string sharedBuildplateId, BuildplateInstancesManager.InstanceType type, CancellationToken cancellationToken)
    {
        SharedBuildplates.SharedBuildplate? sharedBuildplate;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("sharedBuildplates", "", typeof(SharedBuildplates))
                .ExecuteAsync(earthDB, cancellationToken);
            sharedBuildplate = results.Get<SharedBuildplates>("sharedBuildplates").GetSharedBuildplate(sharedBuildplateId);
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        if (sharedBuildplate is null)
        {
            return TypedResults.NotFound();
        }

        string? instanceId = await buildplateInstancesManager.RequestBuildplateInstance(playerId, null, sharedBuildplateId, type, 0, sharedBuildplate.Night);
        if (instanceId is null)
        {
            return TypedResults.InternalServerError();
        }

        BuildplateInstancesManager.InstanceInfo? instanceInfo = buildplateInstancesManager.GetInstanceInfo(instanceId);
        if (instanceInfo is null)
        {
            return TypedResults.InternalServerError();
        }

        BuildplateInstance? buildplateInstance = await InstanceInfoToApiResponse(instanceInfo, cancellationToken);
        if (buildplateInstance is null)
        {
            return TypedResults.InternalServerError();
        }

        return EarthJson(buildplateInstance);
    }

    private async Task<Results<ContentHttpResult, NotFound, BadRequest, InternalServerError>> GetNewEncounterBuildplateInstanceResponse(string encounterId, string tileId, TappablesManager tappablesManager, CancellationToken cancellationToken)
    {
        TappablesManager.Encounter? encounter = tappablesManager.GetEncounterWithId(encounterId, tileId);
        if (encounter is null)
        {
            return TypedResults.NotFound();
        }

        string? instanceId = await buildplateInstancesManager.RequestBuildplateInstance(null, encounterId, encounter.EncounterBuildplateId, BuildplateInstancesManager.InstanceType.ENCOUNTER, encounter.SpawnTime + encounter.ValidFor, false);

        if (instanceId is null)
        {
            return TypedResults.InternalServerError();
        }

        BuildplateInstancesManager.InstanceInfo? instanceInfo = buildplateInstancesManager.GetInstanceInfo(instanceId);
        if (instanceInfo is null)
        {
            return TypedResults.InternalServerError();
        }

        BuildplateInstance? buildplateInstance = await InstanceInfoToApiResponse(instanceInfo, cancellationToken);
        if (buildplateInstance is null)
        {
            return TypedResults.InternalServerError();
        }

        return EarthJson(buildplateInstance);
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    private enum Source
    {
        PLAYER,
        SHARED,
        ENCOUNTER
    }

    private static async Task<BuildplateInstance?> InstanceInfoToApiResponse(BuildplateInstancesManager.InstanceInfo instanceInfo, CancellationToken cancellationToken)
    {
        var (fullsize, gameplayMode, source) = instanceInfo.Type switch
        {
            BuildplateInstancesManager.InstanceType.BUILD => (false, BuildplateInstance.GameplayMetadataR.GameplayModeE.BUILDPLATE, Source.PLAYER),
            BuildplateInstancesManager.InstanceType.PLAY => (true, BuildplateInstance.GameplayMetadataR.GameplayModeE.BUILDPLATE_PLAY, Source.PLAYER),
            BuildplateInstancesManager.InstanceType.SHARED_BUILD => (true, BuildplateInstance.GameplayMetadataR.GameplayModeE.SHARED_BUILDPLATE_PLAY, Source.SHARED),
            BuildplateInstancesManager.InstanceType.SHARED_PLAY => (true, BuildplateInstance.GameplayMetadataR.GameplayModeE.SHARED_BUILDPLATE_PLAY, Source.SHARED),
            BuildplateInstancesManager.InstanceType.ENCOUNTER => (true, BuildplateInstance.GameplayMetadataR.GameplayModeE.ENCOUNTER, Source.ENCOUNTER),
            _ => throw new UnreachableException(),
        };

        int size;
        int offset;
        int scale;
        switch (source)
        {
            case Source.PLAYER:
                {
                    Debug.Assert(instanceInfo.PlayerId is not null);

                    Buildplates.Buildplate? buildplate;
                    try
                    {
                        EarthDB.Results results = await new EarthDB.Query(false)
                            .Get("buildplates", instanceInfo.PlayerId, typeof(Buildplates))
                            .ExecuteAsync(earthDB, cancellationToken);
                        buildplate = results.Get<Buildplates>("buildplates").GetBuildplate(instanceInfo.BuildplateId);
                    }
                    catch (EarthDB.DatabaseException exception)
                    {
                        throw new ServerErrorException(exception);
                    }

                    if (buildplate is null)
                    {
                        return null;
                    }

                    size = buildplate.Size;
                    offset = buildplate.Offset;
                    scale = buildplate.Scale;
                }

                break;
            case Source.SHARED:
                {
                    SharedBuildplates.SharedBuildplate? sharedBuildplate;
                    try
                    {
                        EarthDB.Results results = await new EarthDB.Query(false)
                            .Get("sharedBuildplates", "", typeof(SharedBuildplates))
                            .ExecuteAsync(earthDB, cancellationToken);
                        sharedBuildplate = results.Get<SharedBuildplates>("sharedBuildplates").GetSharedBuildplate(instanceInfo.BuildplateId);
                    }
                    catch (EarthDB.DatabaseException exception)
                    {
                        throw new ServerErrorException(exception);
                    }

                    if (sharedBuildplate is null)
                    {
                        return null;
                    }

                    size = sharedBuildplate.Size;
                    offset = sharedBuildplate.Offset;
                    scale = sharedBuildplate.Scale;
                }

                break;
            case Source.ENCOUNTER:
                {
                    EncounterBuildplates.EncounterBuildplate? encounterBuildplate;

                    try
                    {
                        EarthDB.Results results = await new EarthDB.Query(false)
                            .Get("encounterBuildplates", "", typeof(EncounterBuildplates))
                            .ExecuteAsync(earthDB, cancellationToken);
                        encounterBuildplate = results.Get<EncounterBuildplates>("encounterBuildplates").GetEncounterBuildplate(instanceInfo.BuildplateId);
                    }
                    catch (EarthDB.DatabaseException exception)
                    {
                        throw new ServerErrorException(exception);
                    }

                    if (encounterBuildplate is null)
                    {
                        return null;
                    }

                    size = encounterBuildplate.Size;
                    offset = encounterBuildplate.Offset;
                    scale = encounterBuildplate.Scale;
                }

                break;
            default:
                throw new UnreachableException();
        }

        return new BuildplateInstance(
            instanceInfo.InstanceId,
            "00000000-0000-0000-0000-000000000000",
            "d.projectearth.dev",    // TODO
            instanceInfo.Address,
            instanceInfo.Port,
            instanceInfo.Ready,
            instanceInfo.Ready ? BuildplateInstance.ApplicationStatusE.READY : BuildplateInstance.ApplicationStatusE.UNKNOWN,
            instanceInfo.Ready ? BuildplateInstance.ServerStatusE.RUNNING : BuildplateInstance.ServerStatusE.RUNNING,
            Common.Json.Serialize(new Dictionary<string, object>()
            {
                { "buildplateid", instanceInfo.BuildplateId }
            }),
            new BuildplateInstance.GameplayMetadataR(
                instanceInfo.BuildplateId,
                "00000000-0000-0000-0000-000000000000",
                instanceInfo.PlayerId,
                "2020.1217.02",
                "CK06Yzm2",    // TODO
                new Dimension(size, size),
                new Offset(0, offset, 0),
                !fullsize ? scale : 1,
                fullsize,
                gameplayMode,
                SurfaceOrientation.HORIZONTAL,
                null,
                null,    // TODO
                []
            ),
            "776932eeeb69",
            //new Coordinate(50.99636722700025f, -0.7234904312500047f)
            new Coordinate(0.0f, 0.0f)    // TODO
        );
    }

    private sealed record SharedBuildplateInstanceRequest(
        bool FullSize
    );
}
