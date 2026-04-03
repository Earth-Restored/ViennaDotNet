using Serilog;
using System.Diagnostics;
using System.Text;
using ViennaDotNet.Buildplate.Connector.Model;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Buildplate.Connector.Model;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Common;
using ViennaDotNet.DB.Models.Global;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;
using ViennaDotNet.StaticData;
using Buildplates = ViennaDotNet.DB.Models.Player.Buildplates;
using CICIBIEType = ViennaDotNet.StaticData.Catalog.ItemsCatalogR.Item.BoostInfoR.Effect.TypeE;

namespace ViennaDotNet.ApiServer.Utils;

public sealed class BuildplateInstanceRequestHandler
{
    public static void Start(EarthDB earthDB, EventBusClient eventBusClient, ObjectStoreClient objectStoreClient, Catalog catalog)
        => _ = new BuildplateInstanceRequestHandler(earthDB, eventBusClient, objectStoreClient, catalog);

    private readonly EarthDB _earthDB;
    private readonly ObjectStoreClient _objectStoreClient;
    private readonly Catalog _catalog;
    private static BuildplateInstancesManager BuildplateInstancesManager => Program.buildplateInstancesManager;

    public BuildplateInstanceRequestHandler(EarthDB earthDB, EventBusClient eventBusClient, ObjectStoreClient objectStoreClient, Catalog catalog)
    {
        _earthDB = earthDB;
        _objectStoreClient = objectStoreClient;
        _catalog = catalog;

        RequestHandler requestHandler = eventBusClient.AddRequestHandler("buildplates", new RequestHandler.Handler(
            async request =>
            {
                try
                {
                    switch (request.Type)
                    {
                        case "load":
                            {
                                BuildplateLoadRequest? buildplateLoadRequest = ReadRawRequest<BuildplateLoadRequest>(request.Data);
                                if (buildplateLoadRequest is null)
                                {
                                    return null;
                                }

                                BuildplateLoadResponse? buildplateLoadResponse = await HandleLoad(buildplateLoadRequest.PlayerId, buildplateLoadRequest.BuildplateId);
                                return buildplateLoadResponse is not null ? Json.Serialize(buildplateLoadResponse) : null;
                            }
                        case "loadShared":
                            {
                                SharedBuildplateLoadRequest? sharedBuildplateLoadRequest = ReadRawRequest<SharedBuildplateLoadRequest>(request.Data);
                                if (sharedBuildplateLoadRequest is null)
                                {
                                    return null;
                                }

                                BuildplateLoadResponse? buildplateLoadResponse = await HandleLoadShared(sharedBuildplateLoadRequest.SharedBuildplateId);
                                return buildplateLoadResponse is not null ? Json.Serialize(buildplateLoadResponse) : null;
                            }
                        case "loadEncounter":

                            {
                                EncounterBuildplateLoadRequest? encounterBuildplateLoadRequest = ReadRawRequest<EncounterBuildplateLoadRequest>(request.Data);
                                if (encounterBuildplateLoadRequest is null)
                                {
                                    return null;
                                }

                                BuildplateLoadResponse? buildplateLoadResponse = await HandleLoadEncounter(encounterBuildplateLoadRequest.EncounterBuildplateId);
                                return buildplateLoadResponse is not null ? Json.Serialize(buildplateLoadResponse) : null;
                            }
                        case "saved":
                            {
                                RequestWithInstanceId<WorldSavedMessage>? requestWithInstanceId = ReadRequest<WorldSavedMessage>(request.Data);
                                return requestWithInstanceId is null
                                    ? null
                                    : await HandleSaved(requestWithInstanceId.InstanceId, requestWithInstanceId.Request.DataBase64, request.Timestamp) ? "" : null;
                            }
                        case "playerConnected":
                            {
                                Log.Debug("RequestHandler playerConnected");
                                RequestWithInstanceId<PlayerConnectedRequest>? requestWithInstanceId = ReadRequest<PlayerConnectedRequest>(request.Data);
                                if (requestWithInstanceId is null)
                                    return null;

                                PlayerConnectedResponse? playerConnectedResponse = await HandlePlayerConnected(requestWithInstanceId.InstanceId, requestWithInstanceId.Request);
                                return playerConnectedResponse is not null ? Json.Serialize(playerConnectedResponse) : null;
                            }
                        case "playerDisconnected":
                            {
                                RequestWithInstanceId<PlayerDisconnectedRequest>? requestWithInstanceId = ReadRequest<PlayerDisconnectedRequest>(request.Data);
                                if (requestWithInstanceId is null)
                                    return null;

                                PlayerDisconnectedResponse? playerDisconnectedResponse = await HandlePlayerDisconnected(requestWithInstanceId.InstanceId, requestWithInstanceId.Request, request.Timestamp);
                                return playerDisconnectedResponse is not null ? Json.Serialize(playerDisconnectedResponse) : null;
                            }
                        case "playerDead":
                            {
                                RequestWithInstanceId<string>? requestWithInstanceId = ReadRequest<string>(request.Data);
                                if (requestWithInstanceId is null)
                                {
                                    return null;
                                }

                                bool? respawn = HandlePlayerDead(requestWithInstanceId.InstanceId, requestWithInstanceId.Request, request.Timestamp);
                                return respawn is not null ? Json.Serialize(respawn.Value) : null;
                            }
                        case "getInitialPlayerState":
                            {
                                RequestWithInstanceId<string>? requestWithInstanceId = ReadRequest<string>(request.Data);
                                if (requestWithInstanceId is null)
                                {
                                    return null;
                                }

                                InitialPlayerStateResponse? initialPlayerStateResponse = await HandleGetInitialPlayerState(requestWithInstanceId.InstanceId, requestWithInstanceId.Request, request.Timestamp);
                                return initialPlayerStateResponse is not null ? Json.Serialize(initialPlayerStateResponse) : null;
                            }
                        case "getInventory":
                            {
                                RequestWithInstanceId<string>? requestWithInstanceId = ReadRequest<string>(request.Data);
                                if (requestWithInstanceId is null)
                                {
                                    return null;
                                }

                                InventoryResponse? inventoryResponse = await HandleGetInventory(requestWithInstanceId.InstanceId, requestWithInstanceId.Request);
                                return inventoryResponse is not null ? Json.Serialize(inventoryResponse) : null;
                            }
                        case "inventoryAdd":
                            {
                                RequestWithInstanceId<InventoryAddItemMessage>? requestWithInstanceId = ReadRequest<InventoryAddItemMessage>(request.Data);
                                return requestWithInstanceId is null
                                    ? null
                                    : await HandleInventoryAdd(requestWithInstanceId.InstanceId, requestWithInstanceId.Request, request.Timestamp) ? "" : null;
                            }
                        case "inventoryRemove":
                            {
                                RequestWithInstanceId<InventoryRemoveItemRequest>? requestWithBuildplateId = ReadRequest<InventoryRemoveItemRequest>(request.Data);
                                if (requestWithBuildplateId is null)
                                    return null;

                                object response = await HandleInventoryRemove(requestWithBuildplateId.InstanceId, requestWithBuildplateId.Request);
                                return response is not null ? Json.Serialize(response) : null;
                            }
                        case "inventoryUpdateWear":
                            {
                                RequestWithInstanceId<InventoryUpdateItemWearMessage>? requestWithInstanceId = ReadRequest<InventoryUpdateItemWearMessage>(request.Data);

                                return requestWithInstanceId is null
                                    ? null
                                    : await HandleInventoryUpdateWear(requestWithInstanceId.InstanceId, requestWithInstanceId.Request) ? "" : null;
                            }
                        case "inventorySetHotbar":
                            {
                                RequestWithInstanceId<InventorySetHotbarMessage>? requestWithInstanceId = ReadRequest<InventorySetHotbarMessage>(request.Data);

                                return requestWithInstanceId is null
                                    ? null
                                    : await HandleInventorySetHotbar(requestWithInstanceId.InstanceId, requestWithInstanceId.Request) ? "" : null;
                            }
                        default:
                            Log.Warning($"Unknown BuildplateInstanceRequestHandler request type '{request.Type}'");
                            return null;
                    }
                }
                catch (EarthDB.DatabaseException ex)
                {
                    Log.Error($"Database error while handling request: {ex}");
                    return null;
                }
            },
            () =>
            {
                Log.Fatal("Buildplates event bus request handler error");
                Log.CloseAndFlush();
                Environment.Exit(1);
            }
        ));
    }

    private sealed record BuildplateLoadRequest(
        string PlayerId,
        string BuildplateId
    );

    private sealed record SharedBuildplateLoadRequest(
        string SharedBuildplateId
    );

    private sealed record EncounterBuildplateLoadRequest(
        string EncounterBuildplateId
    );

    private sealed record BuildplateLoadResponse(
        string ServerDataBase64
    );

    private async Task<BuildplateLoadResponse?> HandleLoad(string playerId, string buildplateId)
    {
        EarthDB.Results results = await new EarthDB.Query(false)
            .Get("buildplates", playerId, typeof(Buildplates))
            .ExecuteAsync(_earthDB);
        Buildplates buildplates = results.Get<Buildplates>("buildplates");

        Buildplates.Buildplate? buildplate = buildplates.GetBuildplate(buildplateId);
        if (buildplate is null)
        {
            return null;
        }

        byte[]? serverData = (await _objectStoreClient.Get(buildplate.ServerDataObjectId).Task) as byte[];
        if (serverData is null)
        {
            Log.Error($"Data object {buildplate.ServerDataObjectId} for buildplate {buildplateId} could not be loaded from object store");
            return null;
        }

        string serverDataBase64 = Convert.ToBase64String(serverData);

        return new BuildplateLoadResponse(serverDataBase64);
    }

    private async Task<BuildplateLoadResponse?> HandleLoadShared(string sharedBuildplateId)
    {
        EarthDB.Results results = await new EarthDB.Query(false)
            .Get("sharedBuildplates", "", typeof(SharedBuildplates))
            .ExecuteAsync(_earthDB);
        SharedBuildplates sharedBuildplates = results.Get<SharedBuildplates>("sharedBuildplates");

        SharedBuildplates.SharedBuildplate? sharedBuildplate = sharedBuildplates.GetSharedBuildplate(sharedBuildplateId);
        if (sharedBuildplate is null)
        {
            return null;
        }

        byte[]? serverData = (await _objectStoreClient.Get(sharedBuildplate.ServerDataObjectId).Task) as byte[];
        if (serverData is null)
        {
            Log.Error($"Data object {sharedBuildplate.ServerDataObjectId} for shared buildplate {sharedBuildplateId} could not be loaded from object store");
            return null;
        }

        string serverDataBase64 = Convert.ToBase64String(serverData);

        return new BuildplateLoadResponse(serverDataBase64);
    }

    private async Task<BuildplateLoadResponse?> HandleLoadEncounter(string encounterBuildplateId)
    {
        EarthDB.Results results = await new EarthDB.Query(false)
            .Get("encounterBuildplates", "", typeof(EncounterBuildplates))
            .ExecuteAsync(_earthDB);
        EncounterBuildplates encounterBuildplates = results.Get<EncounterBuildplates>("encounterBuildplates");

        EncounterBuildplates.EncounterBuildplate? encounterBuildplate = encounterBuildplates.GetEncounterBuildplate(encounterBuildplateId);
        if (encounterBuildplate is null)
        {
            return null;
        }

        byte[]? serverData = (await _objectStoreClient.Get(encounterBuildplate.ServerDataObjectId).Task) as byte[];
        if (serverData is null)
        {
            Log.Error($"Data object {encounterBuildplate.ServerDataObjectId} for encounter buildplate {encounterBuildplateId} could not be loaded from object store");
            return null;
        }

        string serverDataBase64 = Convert.ToBase64String(serverData);

        return new BuildplateLoadResponse(serverDataBase64);
    }

    private async Task<bool> HandleSaved(string instanceId, string dataBase64, long timestamp)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = BuildplateInstancesManager.GetInstanceInfo(instanceId);
        if (instanceInfo is null)
        {
            return false;
        }

        if (instanceInfo.Type != BuildplateInstancesManager.InstanceType.BUILD)
        {
            return false;
        }

        string? playerId = instanceInfo.PlayerId;
        string buildplateId = instanceInfo.BuildplateId;

        Debug.Assert(playerId is not null);

        byte[] serverData;
        try
        {
            serverData = Convert.FromBase64String(dataBase64);
        }
        catch
        {
            return false;
        }

        EarthDB.Results results = await new EarthDB.Query(false)
            .Get("buildplates", playerId, typeof(Buildplates))
            .ExecuteAsync(_earthDB);
        Buildplates.Buildplate? buildplateUnsafeForPreviewGenerator = results.Get<Buildplates>("buildplates").GetBuildplate(buildplateId);
        if (buildplateUnsafeForPreviewGenerator is null)
            return false;

        string? preview = BuildplateInstancesManager.GetBuildplatePreview(serverData, buildplateUnsafeForPreviewGenerator.Night);
        if (preview is null)
            Log.Warning("Could not generate preview for buildplate");

        string? serverDataObjectId = (await _objectStoreClient.Store(serverData).Task) as string;
        if (serverDataObjectId is null)
        {
            Log.Error($"Could not store new data object for buildplate {buildplateId} in object store");
            return false;
        }

        string? previewObjectId;
        if (preview is not null)
        {
            previewObjectId = (await _objectStoreClient.Store(Encoding.ASCII.GetBytes(preview)).Task) as string;
            if (previewObjectId is null)
            {
                Log.Warning($"Could not store new preview object for buildplate {buildplateId} in object store");
            }
        }
        else
        {
            previewObjectId = null;
        }

        try
        {
            EarthDB.Results results1 = await new EarthDB.Query(true)
                .Get("buildplates", playerId, typeof(Buildplates))
                .Then(results2 =>
                {
                    Buildplates buildplates = results2.Get<Buildplates>("buildplates");
                    Buildplates.Buildplate? buildplate = buildplates.GetBuildplate(buildplateId);
                    if (buildplate is not null)
                    {
                        string oldServerDataObjectId = buildplate.ServerDataObjectId;

                        buildplate = buildplate with { LastModified = timestamp, ServerDataObjectId = serverDataObjectId };

                        string oldPreviewObjectId;
                        if (previewObjectId is not null)
                        {
                            oldPreviewObjectId = buildplate.PreviewObjectId;
                            buildplate = buildplate with { PreviewObjectId = previewObjectId };
                        }
                        else
                        {
                            oldPreviewObjectId = "";
                        }

                        return new EarthDB.Query(true)
                            .Update("buildplates", playerId, buildplates)
                            .Extra("exists", true)
                            .Extra("oldServerDataObjectId", oldServerDataObjectId)
                            .Extra("oldPreviewObjectId", oldPreviewObjectId);
                    }
                    else
                    {
                        return new EarthDB.Query(false)
                            .Extra("exists", false);
                    }
                })
                .ExecuteAsync(_earthDB);

            bool exists = (bool)results1.GetExtra("exists");
            if (exists)
            {
                string oldServerDataObjectId = (string)results1.GetExtra("oldServerDataObjectId");
                _objectStoreClient.Delete(oldServerDataObjectId);

                string oldPreviewObjectId = (string)results1.GetExtra("oldPreviewObjectId");
                if (!string.IsNullOrEmpty(oldPreviewObjectId))
                    _objectStoreClient.Delete(oldPreviewObjectId);

                Log.Information($"Stored new snapshot for buildplate {buildplateId}");

                return true;
            }
            else
            {
                _objectStoreClient.Delete(serverDataObjectId);
                if (previewObjectId is not null)
                {
                    _objectStoreClient.Delete(previewObjectId);
                }

                return false;
            }
        }
        catch (EarthDB.DatabaseException)
        {
            _objectStoreClient.Delete(serverDataObjectId);
            if (previewObjectId is not null)
            {
                _objectStoreClient.Delete(previewObjectId);
            }

            throw;
        }
    }

    private async Task<PlayerConnectedResponse?> HandlePlayerConnected(string instanceId, PlayerConnectedRequest playerConnectedRequest)
    {
        // TODO: check join code etc.

        BuildplateInstancesManager.InstanceInfo? instanceInfo = BuildplateInstancesManager.GetInstanceInfo(instanceId);

        if (instanceInfo is null)
        {
            return null;
        }

        InventoryResponse? initialInventoryContents;
        switch (instanceInfo.Type)
        {
            case BuildplateInstancesManager.InstanceType.BUILD:
                {
                    initialInventoryContents = null;
                }

                break;
            case BuildplateInstancesManager.InstanceType.PLAY:
                {
                    EarthDB.Results results = await new EarthDB.Query(false)
                        .Get("inventory", playerConnectedRequest.Uuid, typeof(Inventory))
                        .Get("hotbar", playerConnectedRequest.Uuid, typeof(Hotbar))
                        .ExecuteAsync(_earthDB);

                    Inventory inventory = results.Get<Inventory>("inventory");
                    Hotbar hotbar = results.Get<Hotbar>("hotbar");

                    initialInventoryContents = new InventoryResponse(
                        [.. Enumerable.Concat(
                            inventory.StackableItems
                                .Select(item => new InventoryResponse.Item(item.Id, item.Count, null, 0)),
                            inventory.NonStackableItems
                                .SelectMany(item => item.Instances
                                    .Select(instance => new InventoryResponse.Item(item.Id, 1, instance.InstanceId, instance.Wear)))
                        ).Where(item => item.Count > 0)],
                        [.. hotbar.Items.Select(item => item is { Count: > 0 } ? new InventoryResponse.HotbarItem(item.Uuid, item.Count, item.InstanceId) : null)]
                    );
                }

                break;
            case BuildplateInstancesManager.InstanceType.SHARED_BUILD or BuildplateInstancesManager.InstanceType.SHARED_PLAY:

                {
                    EarthDB.Results results = await new EarthDB.Query(false)
                        .Get("sharedBuildplates", "", typeof(SharedBuildplates))
                        .ExecuteAsync(_earthDB);
                    SharedBuildplates sharedBuildplates = results.Get<SharedBuildplates>("sharedBuildplates");
                    SharedBuildplates.SharedBuildplate? sharedBuildplate = sharedBuildplates.GetSharedBuildplate(instanceInfo.BuildplateId);
                    if (sharedBuildplate is null)
                    {
                        return null;
                    }

                    initialInventoryContents = new InventoryResponse(
                        [.. Enumerable.Concat(
                            sharedBuildplate.Hotbar
                                .Where(item => item is { Count: > 0, InstanceId: null })
                                .Collect(() => new Dictionary<string, int>(), (hashMap, hotbarItem) =>
                                {
                                    Debug.Assert(hotbarItem is not null);

                                    hashMap[hotbarItem.Uuid] = hashMap.GetOrDefault(hotbarItem.Uuid, 0) + hotbarItem.Count;
                                }, (hashMap1, hashMap2) =>
                                {
                                    foreach (var (uuid, count) in hashMap2)
                                    {
                                        hashMap1[uuid] = hashMap1.GetOrDefault(uuid) + count;
                                    }
                                })
                                .Select(entry => new InventoryResponse.Item(entry.Key, entry.Value, null, 0)),
                            sharedBuildplate.Hotbar
                                .Where(item => item is { Count: > 0, InstanceId: not null })
                                .Select(item => new InventoryResponse.Item(item!.Uuid, 1, item.InstanceId, item.Wear))
                        )],
                        [.. sharedBuildplate.Hotbar.Select(item => item is { Count: > 0 } ? new InventoryResponse.HotbarItem(item.Uuid, item.Count, item.InstanceId) : null)]
                    );
                }

                break;
            case BuildplateInstancesManager.InstanceType.ENCOUNTER:
                {
                    EarthDB.Results results = await new EarthDB.Query(true)
                        .Get("inventory", playerConnectedRequest.Uuid, typeof(Inventory))
                        .Get("hotbar", playerConnectedRequest.Uuid, typeof(Hotbar))
                        .Then(results1 =>
                        {
                            Inventory inventory = results1.Get<Inventory>("inventory");
                            Hotbar hotbar = results1.Get<Hotbar>("hotbar");

                            var inventoryResponseHotbar = new InventoryResponse.HotbarItem[7];
                            Dictionary<string, int?> inventoryResponseStackableItems = [];
                            LinkedList<InventoryResponse.Item> inventoryResponseNonStackableItems = [];
                            for (int index = 0; index < 7; index++)
                            {
                                Hotbar.Item? item = hotbar.Items[index];
                                if (item is not null)
                                {
                                    if (item.InstanceId is null)
                                    {
                                        inventory.TakeItems(item.Uuid, item.Count);
                                        inventoryResponseStackableItems[item.Uuid] = inventoryResponseStackableItems.GetValueOrDefault(item.Uuid, 0) + item.Count;
                                        inventoryResponseHotbar[index] = new InventoryResponse.HotbarItem(item.Uuid, item.Count, null);
                                    }
                                    else
                                    {
                                        int wear = inventory.TakeItems(item.Uuid, [item.InstanceId])![0].Wear;
                                        inventoryResponseNonStackableItems.AddLast(new InventoryResponse.Item(item.Uuid, 1, item.InstanceId, wear));
                                        inventoryResponseHotbar[index] = new InventoryResponse.HotbarItem(item.Uuid, 1, item.InstanceId);
                                    }
                                }
                            }

                            hotbar.LimitToInventory(inventory);

                            InventoryResponse inventoryResponse = new InventoryResponse(
                            [
                                .. inventoryResponseStackableItems.Select(entry => new InventoryResponse.Item(entry.Key, entry.Value, null, 0)),
                                    .. inventoryResponseNonStackableItems
                            ],
                            inventoryResponseHotbar
                        );
                            return new EarthDB.Query(true)
                                .Update("inventory", playerConnectedRequest.Uuid, inventory)
                                .Update("hotbar", playerConnectedRequest.Uuid, hotbar)
                                .Extra("inventoryResponse", inventoryResponse);
                        })
                        .ExecuteAsync(_earthDB);

                    initialInventoryContents = (InventoryResponse)results.GetExtra("inventoryResponse");
                }

                break;
            default:
                {
                    // shouldn't happen, safe default
                    initialInventoryContents = new InventoryResponse([], new InventoryResponse.HotbarItem[7]);
                }

                break;
        }

        PlayerConnectedResponse playerConnectedResponse = new PlayerConnectedResponse(
            true,
            initialInventoryContents
        );

        return playerConnectedResponse;
    }

    private async Task<PlayerDisconnectedResponse?> HandlePlayerDisconnected(string instanceId, PlayerDisconnectedRequest playerDisconnectedRequest, long timestamp)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = BuildplateInstancesManager.GetInstanceInfo(instanceId);
        if (instanceInfo is null)
        {
            return null;
        }

        bool usesBackpack = instanceInfo.Type == BuildplateInstancesManager.InstanceType.ENCOUNTER;
        if (usesBackpack)
        {
            InventoryResponse? backpackContents = playerDisconnectedRequest.BackpackContents;
            if (backpackContents is null)
            {
                Log.Error("Expected backpack contents in player disconnected request");
                return null;
            }

            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("inventory", playerDisconnectedRequest.PlayerId, typeof(Inventory))
                .Get("journal", playerDisconnectedRequest.PlayerId, typeof(Journal))
                .Then(results1 =>
                {
                    Inventory inventory = results1.Get<Inventory>("inventory");
                    Journal journal = results1.Get<Journal>("journal");

                    LinkedList<string> unlockedJournalItems = [];
                    foreach (InventoryResponse.Item item in backpackContents.Items)
                    {
                        Catalog.ItemsCatalogR.Item? catalogItem = _catalog.ItemsCatalog.GetItem(item.Id);
                        if (catalogItem is null)
                        {
                            Log.Error("Backpack contents contained item that is not in item catalog");
                            continue;
                        }

                        if (!catalogItem.Stackable && item.InstanceId is null)
                        {
                            Log.Error("Backpack contents contained non-stackable item without instance ID");
                            continue;
                        }

                        Debug.Assert(item.Count is not null);
                        if (catalogItem.Stackable)
                        {
                            inventory.AddItems(item.Id, item.Count.Value);
                        }
                        else
                        {
                            Debug.Assert(item.InstanceId is not null);

                            inventory.AddItems(item.Id, [new NonStackableItemInstance(item.InstanceId, item.Wear)]);
                        }

                        if (journal.AddCollectedItem(item.Id, timestamp, item.Count.Value) == 0)
                        {
                            if (catalogItem.JournalEntry is not null)
                            {
                                unlockedJournalItems.AddLast(item.Id);
                            }
                        }

                    }

                    Hotbar hotbar = new Hotbar();
                    for (int index = 0; index < 7; index++)
                    {
                        InventoryResponse.HotbarItem? hotbarItem = backpackContents.Hotbar[index];
                        if (hotbarItem is not null)
                        {
                            hotbar.Items[index] = new Hotbar.Item(hotbarItem.Id, hotbarItem.Count, hotbarItem.InstanceId);
                        }
                    }

                    hotbar.LimitToInventory(inventory);

                    EarthDB.Query query = new EarthDB.Query(true)
                            .Update("inventory", playerDisconnectedRequest.PlayerId, inventory)
                            .Update("hotbar", playerDisconnectedRequest.PlayerId, hotbar)
                            .Update("journal", playerDisconnectedRequest.PlayerId, journal);
                    foreach (string itemId in unlockedJournalItems)
                    {
                        query.Then(TokenUtils.AddToken(playerDisconnectedRequest.PlayerId, new Tokens.JournalItemUnlockedToken(itemId)));
                    }

                    return query;
                })
                .ExecuteAsync(_earthDB);
        }

        return new PlayerDisconnectedResponse();
    }

    private static bool? HandlePlayerDead(string instanceId, string playerId, long currentTime)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = BuildplateInstancesManager.GetInstanceInfo(instanceId);
        return instanceInfo is null
            ? null
            : instanceInfo.Type is BuildplateInstancesManager.InstanceType.BUILD or BuildplateInstancesManager.InstanceType.SHARED_BUILD;
    }

    private sealed record EffectInfo(
        long EndTime,
        Catalog.ItemsCatalogR.Item.BoostInfoR.Effect Effect
    );
    private async Task<InitialPlayerStateResponse?> HandleGetInitialPlayerState(string instanceId, string playerId, long currentTime)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = BuildplateInstancesManager.GetInstanceInfo(instanceId);

        if (instanceInfo is null)
        {
            return null;
        }

        var (useHealth, useBoosts) = instanceInfo.Type switch
        {
            BuildplateInstancesManager.InstanceType.BUILD => (false, false),
            BuildplateInstancesManager.InstanceType.PLAY => (false, true),
            BuildplateInstancesManager.InstanceType.SHARED_BUILD => (false, false),
            BuildplateInstancesManager.InstanceType.SHARED_PLAY => (false, true),
            BuildplateInstancesManager.InstanceType.ENCOUNTER => (true, true),
            _ => (false, false),
        };

        if (!useHealth && !useBoosts)
        {
            return new InitialPlayerStateResponse(20.0f, []);
        }
        else
        {
            if (!useBoosts)
            {
                throw new UnreachableException();
            }

            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("profile", playerId, typeof(Profile))
                .Get("boosts", playerId, typeof(Boosts))
                .ExecuteAsync(_earthDB);
            Profile profile = results.Get<Profile>("profile");
            Boosts boosts = results.Get<Boosts>("boosts");

            float maxHealth = BoostUtils.GetMaxPlayerHealth(boosts, currentTime, _catalog.ItemsCatalog);

            return new InitialPlayerStateResponse(
                useHealth ? float.Min(profile.Health, maxHealth) : maxHealth,
                [.. boosts.ActiveBoosts
                .Where(activeBoost => activeBoost is not null)
                .Where(activeBoost => activeBoost!.StartTime + activeBoost.Duration >= currentTime)
                .SelectMany(activeBoost => _catalog.ItemsCatalog.GetItem(activeBoost!.ItemId)!.BoostInfo!.Effects.Select(effect => new EffectInfo(activeBoost.StartTime + activeBoost.Duration, effect)))
                .Where(effectInfo => effectInfo.Effect.Type is CICIBIEType.ADVENTURE_XP or CICIBIEType.DEFENSE or CICIBIEType.EATING or CICIBIEType.HEALTH or CICIBIEType.MINING_SPEED or CICIBIEType.STRENGTH)
                .Select(effectInfo => new InitialPlayerStateResponse.BoostStatusEffect(
                    effectInfo.Effect.Type switch
                    {
                        CICIBIEType.ADVENTURE_XP => InitialPlayerStateResponse.BoostStatusEffect.TypeE.ADVENTURE_XP,
                        CICIBIEType.DEFENSE => InitialPlayerStateResponse.BoostStatusEffect.TypeE.DEFENSE,
                        CICIBIEType.EATING => InitialPlayerStateResponse.BoostStatusEffect.TypeE.EATING,
                        CICIBIEType.HEALTH => InitialPlayerStateResponse.BoostStatusEffect.TypeE.HEALTH,
                        CICIBIEType.MINING_SPEED => InitialPlayerStateResponse.BoostStatusEffect.TypeE.MINING_SPEED,
                        CICIBIEType.STRENGTH => InitialPlayerStateResponse.BoostStatusEffect.TypeE.STRENGTH,
                        _ => throw new UnreachableException(),
                    },
                    effectInfo.Effect.Value,
                    effectInfo.EndTime - currentTime
                ))]
            );
        }
    }

    private async Task<InventoryResponse?> HandleGetInventory(string instanceId, string requestedInventoryPlayerId)
    {
        EarthDB.Results results = await new EarthDB.Query(false)
            .Get("inventory", requestedInventoryPlayerId, typeof(Inventory))
            .Get("hotbar", requestedInventoryPlayerId, typeof(Hotbar))
            .ExecuteAsync(_earthDB);
        Inventory inventory = results.Get<Inventory>("inventory");
        Hotbar hotbar = results.Get<Hotbar>("hotbar");

        return new InventoryResponse(
            [.. Enumerable.Concat(
                inventory.StackableItems
                    .Select(item => new InventoryResponse.Item(item.Id, item.Count, null, 0)),
                inventory.NonStackableItems
                    .SelectMany(item => item.Instances
                    .Select(instance => new InventoryResponse.Item(item.Id, 1, instance.InstanceId, instance.Wear)))
            ).Where(item => item.Count > 0)],
            [.. hotbar.Items.Select(item => item is not null && item.Count > 0 ? new InventoryResponse.HotbarItem(item.Uuid, item.Count, item.InstanceId) : null)]
        );
    }

    private async Task<bool> HandleInventoryAdd(string instanceId, InventoryAddItemMessage inventoryAddItemMessage, long timestamp)
    {
        Catalog.ItemsCatalogR.Item? catalogItem = _catalog.ItemsCatalog.GetItem(inventoryAddItemMessage.ItemId);
        if (catalogItem is null)
        {
            return false;
        }

        if (!catalogItem.Stackable && inventoryAddItemMessage.InstanceId is null)
        {
            return false;
        }

        EarthDB.Results results = await new EarthDB.Query(true)
            .Get("inventory", inventoryAddItemMessage.PlayerId, typeof(Inventory))
            .Get("journal", inventoryAddItemMessage.PlayerId, typeof(Journal))
            .Then(results1 =>
            {
                Inventory inventory = results1.Get<Inventory>("inventory");
                Journal journal = results1.Get<Journal>("journal");

                if (catalogItem.Stackable)
                {
                    inventory.AddItems(inventoryAddItemMessage.ItemId, inventoryAddItemMessage.Count);
                }
                else
                {
                    inventory.AddItems(inventoryAddItemMessage.ItemId, [new NonStackableItemInstance(inventoryAddItemMessage.InstanceId!, inventoryAddItemMessage.Wear)]);
                }

                bool journalItemUnlocked = false;
                if (journal.AddCollectedItem(inventoryAddItemMessage.ItemId, timestamp, inventoryAddItemMessage.Count) == 0)
                {
                    if (catalogItem.JournalEntry is not null)
                    {
                        journalItemUnlocked = true;
                    }
                }

                EarthDB.Query query = new EarthDB.Query(true)
                    .Update("inventory", inventoryAddItemMessage.PlayerId, inventory)
                    .Update("journal", inventoryAddItemMessage.PlayerId, journal);

                if (journalItemUnlocked)
                {
                    query.Then(TokenUtils.AddToken(inventoryAddItemMessage.PlayerId, new Tokens.JournalItemUnlockedToken(inventoryAddItemMessage.ItemId)));
                }

                return query;
            })
            .ExecuteAsync(_earthDB);

        return true;
    }

    private async Task<object> HandleInventoryRemove(string instanceId, InventoryRemoveItemRequest inventoryRemoveItemRequest)
    {
        EarthDB.Results results = await new EarthDB.Query(true)
            .Get("inventory", inventoryRemoveItemRequest.PlayerId, typeof(Inventory))
            .Get("hotbar", inventoryRemoveItemRequest.PlayerId, typeof(Hotbar))
            .Then(results1 =>
            {
                Inventory inventory = results1.Get<Inventory>("inventory");
                Hotbar hotbar = results1.Get<Hotbar>("hotbar");

                object result;
                if (inventoryRemoveItemRequest.InstanceId is not null)
                {
                    if (inventory.TakeItems(inventoryRemoveItemRequest.ItemId, [inventoryRemoveItemRequest.InstanceId]) is null)
                    {
                        Log.Warning($"Buildplate instance {instanceId} attempted to remove item {inventoryRemoveItemRequest.ItemId} {inventoryRemoveItemRequest.InstanceId} from player {inventoryRemoveItemRequest.PlayerId} that is not in inventory");
                        result = false;
                    }
                    else
                    {
                        result = true;
                    }
                }
                else
                {
                    if (inventory.TakeItems(inventoryRemoveItemRequest.ItemId, inventoryRemoveItemRequest.Count))
                    {
                        result = inventoryRemoveItemRequest.Count;
                    }
                    else
                    {
                        int count = inventory.GetItemCount(inventoryRemoveItemRequest.ItemId);
                        if (!inventory.TakeItems(inventoryRemoveItemRequest.ItemId, count))
                        {
                            count = 0;
                        }

                        Log.Warning($"Buildplate instance {instanceId} attempted to remove item {inventoryRemoveItemRequest.ItemId} {inventoryRemoveItemRequest.Count - count} from player {inventoryRemoveItemRequest.PlayerId} that is not in inventory");
                        result = count;
                    }
                }

                hotbar.LimitToInventory(inventory);

                return new EarthDB.Query(true)
                    .Update("inventory", inventoryRemoveItemRequest.PlayerId, inventory)
                    .Update("hotbar", inventoryRemoveItemRequest.PlayerId, hotbar)
                    .Extra("result", result);
            })
            .ExecuteAsync(_earthDB);

        return results.GetExtra("result");
    }

    private async Task<bool> HandleInventoryUpdateWear(string instanceId, InventoryUpdateItemWearMessage inventoryUpdateItemWearMessage)
    {
        EarthDB.Results results = await new EarthDB.Query(true)
            .Get("inventory", inventoryUpdateItemWearMessage.PlayerId, typeof(Inventory))
            .Then(results1 =>
            {
                Inventory inventory = results1.Get<Inventory>("inventory");

                NonStackableItemInstance? nonStackableItemInstance = inventory.GetItemInstance(inventoryUpdateItemWearMessage.ItemId, inventoryUpdateItemWearMessage.InstanceId);
                if (nonStackableItemInstance is not null)
                {
                    // TODO: make NonStackableItemInstance mutable instead of doing this
                    if (inventory.TakeItems(inventoryUpdateItemWearMessage.ItemId, [inventoryUpdateItemWearMessage.InstanceId]) is null)
                    {
                        throw new InvalidOperationException();
                    }

                    inventory.AddItems(inventoryUpdateItemWearMessage.ItemId, [new NonStackableItemInstance(inventoryUpdateItemWearMessage.InstanceId, inventoryUpdateItemWearMessage.Wear)]);
                }
                else
                    Log.Warning($"Buildplate instance {instanceId} attempted to update item wear for item {inventoryUpdateItemWearMessage.ItemId} {inventoryUpdateItemWearMessage.InstanceId} player {inventoryUpdateItemWearMessage.PlayerId} that is not in inventory");

                return new EarthDB.Query(true)
                    .Update("inventory", inventoryUpdateItemWearMessage.PlayerId, inventory);
            })
            .ExecuteAsync(_earthDB);
        return true;
    }

    private async Task<bool> HandleInventorySetHotbar(string instanceId, InventorySetHotbarMessage inventorySetHotbarMessage)
    {
        EarthDB.Results results = await new EarthDB.Query(true)
            .Get("inventory", inventorySetHotbarMessage.PlayerId, typeof(Inventory))
            .Then(results1 =>
            {
                Inventory inventory = results1.Get<Inventory>("inventory");

                Hotbar hotbar = new Hotbar();
                for (int index = 0; index < hotbar.Items.Length; index++)
                {
                    InventorySetHotbarMessage.Item item = inventorySetHotbarMessage.Items[index];
                    hotbar.Items[index] = item is not null ? new Hotbar.Item(item.ItemId, item.Count, item.InstanceId) : null;
                }

                hotbar.LimitToInventory(inventory);

                return new EarthDB.Query(true)
                    .Update("hotbar", inventorySetHotbarMessage.PlayerId, hotbar);
            })
            .ExecuteAsync(_earthDB);

        return true;
    }

    private static RequestWithInstanceId<T>? ReadRequest<T>(string str)
    {
        try
        {
            RequestWithInstanceId<T>? request = Json.Deserialize<RequestWithInstanceId<T>>(str);
            return request;
        }
        catch (Exception ex)
        {
            Log.Error($"Bad JSON in buildplates event bus request: {ex}");
            return null;
        }
    }

    private static T? ReadRawRequest<T>(string str)
    {
        try
        {
            T? request = Json.Deserialize<T>(str);
            return request;
        }
        catch (Exception ex)
        {
            Log.Error($"Bad JSON in buildplates event bus request: {ex}");
            return default;
        }
    }

    private sealed record RequestWithInstanceId<T>(
        string InstanceId,
        T Request
    );
}
