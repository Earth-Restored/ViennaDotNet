using Newtonsoft.Json;
using Serilog;
using System.Diagnostics;
using System.Text;
using ViennaDotNet.Buildplate.Connector.Model;
using ViennaDotNet.Common.Buildplate.Connector.Model;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Common;
using ViennaDotNet.DB.Models.Global;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Utils;

public sealed class BuildplateInstanceRequestHandler
{
    public static void start(EarthDB earthDB, EventBusClient eventBusClient, ObjectStoreClient objectStoreClient, Catalog catalog)
        => _ = new BuildplateInstanceRequestHandler(earthDB, eventBusClient, objectStoreClient, catalog);

    private readonly EarthDB earthDB;
    private readonly ObjectStoreClient objectStoreClient;
    private readonly Catalog catalog;
    private static BuildplateInstancesManager buildplateInstancesManager => Program.buildplateInstancesManager;

    public BuildplateInstanceRequestHandler(EarthDB earthDB, EventBusClient eventBusClient, ObjectStoreClient objectStoreClient, Catalog catalog)
    {
        this.earthDB = earthDB;
        this.objectStoreClient = objectStoreClient;
        this.catalog = catalog;

        RequestHandler requestHandler = eventBusClient.addRequestHandler("buildplates", new RequestHandler.Handler(
            request =>
            {
                try
                {
                    switch (request.type)
                    {
                        case "load":
                            {
                                BuildplateLoadRequest? buildplateLoadRequest = readRawRequest<BuildplateLoadRequest>(request.data);
                                if (buildplateLoadRequest is null)
                                    return null;

                                BuildplateLoadResponse? buildplateLoadResponse = handleLoad(buildplateLoadRequest.playerId, buildplateLoadRequest.buildplateId);
                                return buildplateLoadResponse is not null ? JsonConvert.SerializeObject(buildplateLoadResponse) : null;
                            }
                        case "loadShared":
                            {
                                SharedBuildplateLoadRequest? sharedBuildplateLoadRequest = readRawRequest<SharedBuildplateLoadRequest>(request.data);
                                if (sharedBuildplateLoadRequest is null)
                                {
                                    return null;
                                }

                                BuildplateLoadResponse? buildplateLoadResponse = handleLoadShared(sharedBuildplateLoadRequest.sharedBuildplateId);
                                return buildplateLoadResponse is not null ? JsonConvert.SerializeObject(buildplateLoadResponse) : null;
                            }
                        case "loadEncounter":

                            {
                                EncounterBuildplateLoadRequest? encounterBuildplateLoadRequest = readRawRequest<EncounterBuildplateLoadRequest>(request.data);
                                if (encounterBuildplateLoadRequest is null)
                                {
                                    return null;
                                }

                                BuildplateLoadResponse? buildplateLoadResponse = handleLoadEncounter(encounterBuildplateLoadRequest.encounterBuildplateId);
                                return buildplateLoadResponse is not null ? JsonConvert.SerializeObject(buildplateLoadResponse) : null;
                            }
                        case "saved":
                            {
                                RequestWithInstanceId<WorldSavedMessage>? requestWithInstanceId = readRequest<WorldSavedMessage>(request.data);
                                if (requestWithInstanceId is null)
                                    return null;

                                return handleSaved(requestWithInstanceId.instanceId, requestWithInstanceId.request.dataBase64, request.timestamp) ? "" : null;
                            }
                        case "playerConnected":
                            {
                                Log.Debug("RequestHandler playerConnected");
                                RequestWithInstanceId<PlayerConnectedRequest>? requestWithInstanceId = readRequest<PlayerConnectedRequest>(request.data);
                                if (requestWithInstanceId is null)
                                    return null;

                                PlayerConnectedResponse? playerConnectedResponse = handlePlayerConnected(requestWithInstanceId.instanceId, requestWithInstanceId.request);
                                return playerConnectedResponse != null ? JsonConvert.SerializeObject(playerConnectedResponse) : null;
                            }
                        case "playerDisconnected":
                            {
                                RequestWithInstanceId<PlayerDisconnectedRequest>? requestWithInstanceId = readRequest<PlayerDisconnectedRequest>(request.data);
                                if (requestWithInstanceId is null)
                                    return null;

                                PlayerDisconnectedResponse? playerDisconnectedResponse = handlePlayerDisconnected(requestWithInstanceId.instanceId, requestWithInstanceId.request, request.timestamp);
                                return playerDisconnectedResponse != null ? JsonConvert.SerializeObject(playerDisconnectedResponse) : null;
                            }
                        case "getInventory":
                            {
                                RequestWithInstanceId<string>? requestWithInstanceId = readRequest<string>(request.data);
                                if (requestWithInstanceId is null)
                                    return null;

                                InventoryResponse? inventoryResponse = handleGetInventory(requestWithInstanceId.instanceId, requestWithInstanceId.request);
                                return inventoryResponse is not null ? JsonConvert.SerializeObject(inventoryResponse) : null;
                            }
                        case "inventoryAdd":
                            {
                                RequestWithInstanceId<InventoryAddItemMessage>? requestWithInstanceId = readRequest<InventoryAddItemMessage>(request.data);
                                if (requestWithInstanceId is null)
                                    return null;

                                return handleInventoryAdd(requestWithInstanceId.instanceId, requestWithInstanceId.request, request.timestamp) ? "" : null;
                            }
                        case "inventoryRemove":
                            {
                                RequestWithInstanceId<InventoryRemoveItemRequest>? requestWithBuildplateId = readRequest<InventoryRemoveItemRequest>(request.data);
                                if (requestWithBuildplateId is null)
                                    return null;

                                object response = handleInventoryRemove(requestWithBuildplateId.instanceId, requestWithBuildplateId.request);
                                return response is not null ? JsonConvert.SerializeObject(response) : null;
                            }
                        case "inventoryUpdateWear":
                            {
                                RequestWithInstanceId<InventoryUpdateItemWearMessage>? requestWithInstanceId = readRequest<InventoryUpdateItemWearMessage>(request.data);
                                if (requestWithInstanceId is null)
                                    return null;

                                return handleInventoryUpdateWear(requestWithInstanceId.instanceId, requestWithInstanceId.request) ? "" : null;
                            }
                        case "inventorySetHotbar":
                            {
                                RequestWithInstanceId<InventorySetHotbarMessage>? requestWithInstanceId = readRequest<InventorySetHotbarMessage>(request.data);
                                if (requestWithInstanceId is null)
                                {
                                    return null;
                                }

                                return handleInventorySetHotbar(requestWithInstanceId.instanceId, requestWithInstanceId.request) ? "" : null;
                            }
                        default:
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
        string playerId,
        string buildplateId
    );

    private sealed record SharedBuildplateLoadRequest(
        string sharedBuildplateId
    );

    private sealed record EncounterBuildplateLoadRequest(
        string encounterBuildplateId
    );

    private sealed record BuildplateLoadResponse(
        string serverDataBase64
    );

    private BuildplateLoadResponse? handleLoad(string playerId, string buildplateId)
    {
        EarthDB.Results results = new EarthDB.Query(false)
            .Get("buildplates", playerId, typeof(Buildplates))
            .Execute(earthDB);
        Buildplates buildplates = (Buildplates)results.Get("buildplates").Value;

        Buildplates.Buildplate? buildplate = buildplates.getBuildplate(buildplateId);
        if (buildplate == null)
            return null;

        // TODO: when event bus code is made async await here
        byte[]? serverData = (byte[]?)objectStoreClient.get(buildplate.serverDataObjectId).Task.Result;
        if (serverData == null)
        {
            Log.Error($"Data object {buildplate.serverDataObjectId} for buildplate {buildplateId} could not be loaded from object store");
            return null;
        }

        string serverDataBase64 = Convert.ToBase64String(serverData);

        return new BuildplateLoadResponse(serverDataBase64);
    }

    private BuildplateLoadResponse? handleLoadShared(string sharedBuildplateId)
    {
        EarthDB.Results results = new EarthDB.Query(false)
            .Get("sharedBuildplates", "", typeof(SharedBuildplates))
            .Execute(earthDB);
        SharedBuildplates sharedBuildplates = (SharedBuildplates)results.Get("sharedBuildplates").Value;

        SharedBuildplates.SharedBuildplate? sharedBuildplate = sharedBuildplates.getSharedBuildplate(sharedBuildplateId);
        if (sharedBuildplate is null)
        {
            return null;
        }

        // TODO: when event bus code is made async await here
        byte[]? serverData = objectStoreClient.get(sharedBuildplate.serverDataObjectId).Task.Result as byte[];
        if (serverData is null)
        {
            Log.Error($"Data object {sharedBuildplate.serverDataObjectId} for shared buildplate {sharedBuildplateId} could not be loaded from object store");
            return null;
        }

        string serverDataBase64 = Convert.ToBase64String(serverData);

        return new BuildplateLoadResponse(serverDataBase64);
    }

    private BuildplateLoadResponse? handleLoadEncounter(string encounterBuildplateId)
    {
        EarthDB.Results results = new EarthDB.Query(false)
            .Get("encounterBuildplates", "", typeof(EncounterBuildplates))
            .Execute(earthDB);
        EncounterBuildplates encounterBuildplates = (EncounterBuildplates)results.Get("encounterBuildplates").Value;

        EncounterBuildplates.EncounterBuildplate? encounterBuildplate = encounterBuildplates.getEncounterBuildplate(encounterBuildplateId);
        if (encounterBuildplate is null)
        {
            return null;
        }

        byte[]? serverData = objectStoreClient.get(encounterBuildplate.serverDataObjectId).Task.Result as byte[];
        if (serverData is null)
        {
            Log.Error($"Data object {encounterBuildplate.serverDataObjectId} for encounter buildplate {encounterBuildplateId} could not be loaded from object store");
            return null;
        }

        string serverDataBase64 = Convert.ToBase64String(serverData);

        return new BuildplateLoadResponse(serverDataBase64);
    }

    private bool handleSaved(string instanceId, string dataBase64, long timestamp)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = buildplateInstancesManager.getInstanceInfo(instanceId);
        if (instanceInfo is null)
        {
            return false;
        }

        if (instanceInfo.type != BuildplateInstancesManager.InstanceType.BUILD)
        {
            return false;
        }

        string? playerId = instanceInfo.playerId;
        string buildplateId = instanceInfo.buildplateId;

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

        EarthDB.Results results = new EarthDB.Query(false)
            .Get("buildplates", playerId, typeof(Buildplates))
            .Execute(earthDB);
        Buildplates.Buildplate? buildplateUnsafeForPreviewGenerator = ((Buildplates)results.Get("buildplates").Value).getBuildplate(buildplateId);
        if (buildplateUnsafeForPreviewGenerator == null)
            return false;

        string? preview = buildplateInstancesManager.getBuildplatePreview(serverData, buildplateUnsafeForPreviewGenerator.night);
        if (preview == null)
            Log.Warning("Could not generate preview for buildplate");

        // TODO: when event bus code is made async await here
        string? serverDataObjectId = (string?)objectStoreClient.store(serverData).Task.Result;
        if (serverDataObjectId == null)
        {
            Log.Error($"Could not store new data object for buildplate {buildplateId} in object store");
            return false;
        }

        string? previewObjectId;
        if (preview != null)
        {
            // TODO: when event bus code is made async await here
            previewObjectId = (string?)objectStoreClient.store(Encoding.ASCII.GetBytes(preview)).Task.Result;
            if (previewObjectId == null)
                Log.Warning($"Could not store new preview object for buildplate {buildplateId} in object store");
        }
        else
            previewObjectId = null;

        try
        {
            EarthDB.Results results1 = new EarthDB.Query(true)
                    .Get("buildplates", playerId, typeof(Buildplates))
                .Then(results2 =>
                {
                    Buildplates buildplates = (Buildplates)results2.Get("buildplates").Value;
                    Buildplates.Buildplate? buildplate = buildplates.getBuildplate(buildplateId);
                    if (buildplate != null)
                    {
                        buildplate.lastModified = timestamp;

                        string oldServerDataObjectId = buildplate.serverDataObjectId;
                        buildplate.serverDataObjectId = serverDataObjectId;

                        string oldPreviewObjectId;
                        if (previewObjectId != null)
                        {
                            oldPreviewObjectId = buildplate.previewObjectId;
                            buildplate.previewObjectId = previewObjectId;
                        }
                        else
                            oldPreviewObjectId = "";

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
                .Execute(earthDB);

            bool exists = (bool)results1.getExtra("exists");
            if (exists)
            {
                string oldServerDataObjectId = (string)results1.getExtra("oldServerDataObjectId");
                objectStoreClient.delete(oldServerDataObjectId);

                string oldPreviewObjectId = (string)results1.getExtra("oldPreviewObjectId");
                if (!string.IsNullOrEmpty(oldPreviewObjectId))
                    objectStoreClient.delete(oldPreviewObjectId);

                Log.Information($"Stored new snapshot for buildplate {buildplateId}");

                return true;
            }
            else
            {
                objectStoreClient.delete(serverDataObjectId);
                if (previewObjectId != null)
                {
                    objectStoreClient.delete(previewObjectId);
                }

                return false;
            }
        }
        catch (EarthDB.DatabaseException)
        {
            objectStoreClient.delete(serverDataObjectId);
            if (previewObjectId != null)
            {
                objectStoreClient.delete(previewObjectId);
            }

            throw;
        }
    }

    private PlayerConnectedResponse? handlePlayerConnected(string instanceId, PlayerConnectedRequest playerConnectedRequest)
    {
        // TODO: check join code etc.

        BuildplateInstancesManager.InstanceInfo? instanceInfo = buildplateInstancesManager.getInstanceInfo(instanceId);

        if (instanceInfo is null)
        {
            return null;
        }

        InventoryResponse? initialInventoryContents;
        switch (instanceInfo.type)
        {
            case BuildplateInstancesManager.InstanceType.BUILD:
                {
                    initialInventoryContents = null;
                }

                break;
            case BuildplateInstancesManager.InstanceType.PLAY:
                {
                    EarthDB.Results results = new EarthDB.Query(false)
                        .Get("inventory", playerConnectedRequest.uuid, typeof(Inventory))
                        .Get("hotbar", playerConnectedRequest.uuid, typeof(Hotbar))
                        .Execute(earthDB);

                    Inventory inventory = (Inventory)results.Get("inventory").Value;
                    Hotbar hotbar = (Hotbar)results.Get("hotbar").Value;

                    initialInventoryContents = new InventoryResponse(
                        [.. Enumerable.Concat(
                            inventory.getStackableItems()
                                .Select(item => new InventoryResponse.Item(item.id, item.count, null, 0)),
                            inventory.getNonStackableItems()
                                .SelectMany(item => item.instances
                                    .Select(instance => new InventoryResponse.Item(item.id, 1, instance.instanceId, instance.wear)))
                        ).Where(item => item.count > 0)],
                        [.. hotbar.items.Select(item => item is { count: > 0 } ? new InventoryResponse.HotbarItem(item.uuid, item.count, item.instanceId) : null)]
                    );
                }

                break;
            case BuildplateInstancesManager.InstanceType.SHARED_BUILD or BuildplateInstancesManager.InstanceType.SHARED_PLAY:

                {
                    EarthDB.Results results = new EarthDB.Query(false)
                        .Get("sharedBuildplates", "", typeof(SharedBuildplates))
                        .Execute(earthDB);
                    SharedBuildplates sharedBuildplates = (SharedBuildplates)results.Get("sharedBuildplates").Value;
                    SharedBuildplates.SharedBuildplate? sharedBuildplate = sharedBuildplates.getSharedBuildplate(instanceInfo.buildplateId);
                    if (sharedBuildplate is null)
                    {
                        return null;
                    }

                    initialInventoryContents = new InventoryResponse(
                        [.. Enumerable.Concat(
                            sharedBuildplate.hotbar
                                .Where(item => item is { count: > 0, instanceId: null })
                                .Collect(() => new Dictionary<string, int>(), (hashMap, hotbarItem) =>
                                {
                                    Debug.Assert(hotbarItem is not null);

                                    hashMap[hotbarItem.uuid] = hashMap.GetOrDefault(hotbarItem.uuid, 0) + hotbarItem.count;
                                }, (hashMap1, hashMap2) =>
                                {
                                    foreach (var (uuid, count) in hashMap2)
                                    {
                                        hashMap1[uuid] = hashMap1.GetOrDefault(uuid) + count;
                                    }
                                })
                                .Select(entry => new InventoryResponse.Item(entry.Key, entry.Value, null, 0)),
                            sharedBuildplate.hotbar
                                .Where(item => item is { count: > 0, instanceId: not null })
                                .Select(item => new InventoryResponse.Item(item!.uuid, 1, item.instanceId, item.wear))
                        )],
                        [.. sharedBuildplate.hotbar.Select(item => item is { count: > 0 } ? new InventoryResponse.HotbarItem(item.uuid, item.count, item.instanceId) : null)]
                    );
                }

                break;
            case BuildplateInstancesManager.InstanceType.ENCOUNTER:
                {
                    EarthDB.Results results = new EarthDB.Query(true)
                        .Get("inventory", playerConnectedRequest.uuid, typeof(Inventory))
                        .Get("hotbar", playerConnectedRequest.uuid, typeof(Hotbar))
                        .Then(results1 =>
                        {
                            Inventory inventory = (Inventory)results1.Get("inventory").Value;
                            Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                            var inventoryResponseHotbar = new InventoryResponse.HotbarItem[7];
                            Dictionary<string, int?> inventoryResponseStackableItems = [];
                            LinkedList<InventoryResponse.Item> inventoryResponseNonStackableItems = [];
                            for (int index = 0; index < 7; index++)
                            {
                                Hotbar.Item? item = hotbar.items[index];
                                if (item is not null)
                                {
                                    if (item.instanceId is null)
                                    {
                                        inventory.takeItems(item.uuid, item.count);
                                        inventoryResponseStackableItems[item.uuid] = inventoryResponseStackableItems.GetValueOrDefault(item.uuid, 0) + item.count;
                                        inventoryResponseHotbar[index] = new InventoryResponse.HotbarItem(item.uuid, item.count, null);
                                    }
                                    else
                                    {
                                        int wear = inventory.takeItems(item.uuid, [item.instanceId])![0].wear;
                                        inventoryResponseNonStackableItems.AddLast(new InventoryResponse.Item(item.uuid, 1, item.instanceId, wear));
                                        inventoryResponseHotbar[index] = new InventoryResponse.HotbarItem(item.uuid, 1, item.instanceId);
                                    }
                                }
                            }

                            hotbar.limitToInventory(inventory);

                            InventoryResponse inventoryResponse = new InventoryResponse(
                            [
                                .. inventoryResponseStackableItems.Select(entry => new InventoryResponse.Item(entry.Key, entry.Value, null, 0)),
                                    .. inventoryResponseNonStackableItems
                            ],
                            inventoryResponseHotbar
                        );
                            return new EarthDB.Query(true)
                                .Update("inventory", playerConnectedRequest.uuid, inventory)
                                .Update("hotbar", playerConnectedRequest.uuid, hotbar)
                                .Extra("inventoryResponse", inventoryResponse);
                        })
                        .Execute(earthDB);

                    initialInventoryContents = (InventoryResponse)results.getExtra("inventoryResponse");
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

    private PlayerDisconnectedResponse? handlePlayerDisconnected(string instanceId, PlayerDisconnectedRequest playerDisconnectedRequest, long timestamp)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = buildplateInstancesManager.getInstanceInfo(instanceId);
        if (instanceInfo is null)
        {
            return null;
        }

        bool usesBackpack = instanceInfo.type == BuildplateInstancesManager.InstanceType.ENCOUNTER;
        if (usesBackpack)
        {
            InventoryResponse? backpackContents = playerDisconnectedRequest.backpackContents;
            if (backpackContents is null)
            {
                Log.Error("Expected backpack contents in player disconnected request");
                return null;
            }

            EarthDB.Results results = new EarthDB.Query(true)
                .Get("inventory", playerDisconnectedRequest.playerId, typeof(Inventory))
                .Get("journal", playerDisconnectedRequest.playerId, typeof(Journal))
                .Then(results1 =>
                {
                    Inventory inventory = (Inventory)results1.Get("inventory").Value;
                    Journal journal = (Journal)results1.Get("journal").Value;

                    LinkedList<string> unlockedJournalItems = [];
                    foreach (InventoryResponse.Item item in backpackContents.items)
                    {
                        Catalog.ItemsCatalog.Item? catalogItem = catalog.itemsCatalog.getItem(item.id);
                        if (catalogItem == null)
                        {
                            Log.Error("Backpack contents contained item that is not in item catalog");
                            continue;
                        }

                        if (!catalogItem.stackable && item.instanceId is null)
                        {
                            Log.Error("Backpack contents contained non-stackable item without instance ID");
                            continue;
                        }

                        Debug.Assert(item.count is not null);
                        if (catalogItem.stackable)
                        {
                            inventory.addItems(item.id, item.count.Value);
                        }
                        else
                        {
                            Debug.Assert(item.instanceId is not null);

                            inventory.addItems(item.id, [new NonStackableItemInstance(item.instanceId, item.wear)]);
                        }

                        if (journal.addCollectedItem(item.id, timestamp, item.count.Value) == 0)
                        {
                            if (catalogItem.journalEntry is not null)
                            {
                                unlockedJournalItems.AddLast(item.id);
                            }
                        }

                    }

                    Hotbar hotbar = new Hotbar();
                    for (int index = 0; index < 7; index++)
                    {
                        InventoryResponse.HotbarItem? hotbarItem = backpackContents.hotbar[index];
                        if (hotbarItem is not null)
                        {
                            hotbar.items[index] = new Hotbar.Item(hotbarItem.id, hotbarItem.count, hotbarItem.instanceId);
                        }
                    }

                    hotbar.limitToInventory(inventory);

                    EarthDB.Query query = new EarthDB.Query(true)
                            .Update("inventory", playerDisconnectedRequest.playerId, inventory)
                            .Update("hotbar", playerDisconnectedRequest.playerId, hotbar)
                            .Update("journal", playerDisconnectedRequest.playerId, journal);
                    foreach (string itemId in unlockedJournalItems)
                    {
                        query.Then(TokenUtils.addToken(playerDisconnectedRequest.playerId, new Tokens.JournalItemUnlockedToken(itemId)));
                    }

                    return query;
                })
                .Execute(earthDB);
        }

        return new PlayerDisconnectedResponse();
    }

    private InventoryResponse? handleGetInventory(string instanceId, string requestedInventoryPlayerId)
    {
        EarthDB.Results results = new EarthDB.Query(false)
            .Get("inventory", requestedInventoryPlayerId, typeof(Inventory))
            .Get("hotbar", requestedInventoryPlayerId, typeof(Hotbar))
            .Execute(earthDB);
        Inventory inventory = (Inventory)results.Get("inventory").Value;
        Hotbar hotbar = (Hotbar)results.Get("hotbar").Value;

        return new InventoryResponse(
            [.. Enumerable.Concat(
                inventory.getStackableItems()
                    .Select(item => new InventoryResponse.Item(item.id, item.count, null, 0)),
                inventory.getNonStackableItems()
                    .SelectMany(item => item.instances
                    .Select(instance => new InventoryResponse.Item(item.id, 1, instance.instanceId, instance.wear)))
            ).Where(item => item.count > 0)],
            [.. hotbar.items.Select(item => item != null && item.count > 0 ? new InventoryResponse.HotbarItem(item.uuid, item.count, item.instanceId) : null)]
        );
    }

    private bool handleInventoryAdd(string instanceId, InventoryAddItemMessage inventoryAddItemMessage, long timestamp)
    {
        Catalog.ItemsCatalog.Item? catalogItem = catalog.itemsCatalog.getItem(inventoryAddItemMessage.itemId);
        if (catalogItem == null)
            return false;
        if (!catalogItem.stackable && inventoryAddItemMessage.instanceId == null)
            return false;

        EarthDB.Results results = new EarthDB.Query(true)
            .Get("inventory", inventoryAddItemMessage.playerId, typeof(Inventory))
            .Get("journal", inventoryAddItemMessage.playerId, typeof(Journal))
            .Then(results1 =>
            {
                Inventory inventory = (Inventory)results1.Get("inventory").Value;
                Journal journal = (Journal)results1.Get("journal").Value;

                if (catalogItem.stackable)
                    inventory.addItems(inventoryAddItemMessage.itemId, inventoryAddItemMessage.count);
                else
                    inventory.addItems(inventoryAddItemMessage.itemId, [new NonStackableItemInstance(inventoryAddItemMessage.instanceId!, inventoryAddItemMessage.wear)]);

                bool journalItemUnlocked = false;
                if (journal.addCollectedItem(inventoryAddItemMessage.itemId, timestamp, inventoryAddItemMessage.count) == 0)
                {
                    if (catalogItem.journalEntry is not null)
                    {
                        journalItemUnlocked = true;
                    }
                }

                EarthDB.Query query = new EarthDB.Query(true)
                    .Update("inventory", inventoryAddItemMessage.playerId, inventory)
                    .Update("journal", inventoryAddItemMessage.playerId, journal);

                if (journalItemUnlocked)
                {
                    query.Then(TokenUtils.addToken(inventoryAddItemMessage.playerId, new Tokens.JournalItemUnlockedToken(inventoryAddItemMessage.itemId)));
                }

                return query;
            })
            .Execute(earthDB);

        return true;
    }

    private object handleInventoryRemove(string instanceId, InventoryRemoveItemRequest inventoryRemoveItemRequest)
    {
        EarthDB.Results results = new EarthDB.Query(true)
            .Get("inventory", inventoryRemoveItemRequest.playerId, typeof(Inventory))
            .Get("hotbar", inventoryRemoveItemRequest.playerId, typeof(Hotbar))
            .Then(results1 =>
            {
                Inventory inventory = (Inventory)results1.Get("inventory").Value;
                Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                object result;
                if (inventoryRemoveItemRequest.instanceId != null)
                {
                    if (inventory.takeItems(inventoryRemoveItemRequest.itemId, [inventoryRemoveItemRequest.instanceId]) == null)
                    {
                        Log.Warning($"Buildplate instance {instanceId} attempted to remove item {inventoryRemoveItemRequest.itemId} {inventoryRemoveItemRequest.instanceId} from player {inventoryRemoveItemRequest.playerId} that is not in inventory");
                        result = false;
                    }
                    else
                        result = true;
                }
                else
                {
                    if (inventory.takeItems(inventoryRemoveItemRequest.itemId, inventoryRemoveItemRequest.count))
                        result = inventoryRemoveItemRequest.count;
                    else
                    {
                        int count = inventory.getItemCount(inventoryRemoveItemRequest.itemId);
                        if (!inventory.takeItems(inventoryRemoveItemRequest.itemId, count))
                            count = 0;

                        Log.Warning($"Buildplate instance {instanceId} attempted to remove item {inventoryRemoveItemRequest.itemId} {inventoryRemoveItemRequest.count - count} from player {inventoryRemoveItemRequest.playerId} that is not in inventory");
                        result = count;
                    }
                }

                hotbar.limitToInventory(inventory);

                return new EarthDB.Query(true)
                    .Update("inventory", inventoryRemoveItemRequest.playerId, inventory)
                    .Update("hotbar", inventoryRemoveItemRequest.playerId, hotbar)
                    .Extra("result", result);
            })
            .Execute(earthDB);

        return results.getExtra("result");
    }

    private bool handleInventoryUpdateWear(string instanceId, InventoryUpdateItemWearMessage inventoryUpdateItemWearMessage)
    {
        EarthDB.Results results = new EarthDB.Query(true)
            .Get("inventory", inventoryUpdateItemWearMessage.playerId, typeof(Inventory))
            .Then(results1 =>
            {
                Inventory inventory = (Inventory)results1.Get("inventory").Value;

                NonStackableItemInstance? nonStackableItemInstance = inventory.getItemInstance(inventoryUpdateItemWearMessage.itemId, inventoryUpdateItemWearMessage.instanceId);
                if (nonStackableItemInstance != null)
                {
                    // TODO: make NonStackableItemInstance mutable instead of doing this
                    if (inventory.takeItems(inventoryUpdateItemWearMessage.itemId, [inventoryUpdateItemWearMessage.instanceId]) == null)
                        throw new InvalidOperationException();

                    inventory.addItems(inventoryUpdateItemWearMessage.itemId, [new NonStackableItemInstance(inventoryUpdateItemWearMessage.instanceId, inventoryUpdateItemWearMessage.wear)]);
                }
                else
                    Log.Warning("Buildplate instance {instanceId} attempted to update item wear for item {inventoryUpdateItemWearMessage.itemId()} {inventoryUpdateItemWearMessage.instanceId()} player {inventoryUpdateItemWearMessage.playerId()} that is not in inventory");

                return new EarthDB.Query(true)
                    .Update("inventory", inventoryUpdateItemWearMessage.playerId, inventory);
            })
            .Execute(earthDB);
        return true;
    }

    private bool handleInventorySetHotbar(string instanceId, InventorySetHotbarMessage inventorySetHotbarMessage)
    {
        EarthDB.Results results = new EarthDB.Query(true)
            .Get("inventory", inventorySetHotbarMessage.playerId, typeof(Inventory))
            .Then(results1 =>
            {
                Inventory inventory = (Inventory)results1.Get("inventory").Value;

                Hotbar hotbar = new Hotbar();
                for (int index = 0; index < hotbar.items.Length; index++)
                {
                    InventorySetHotbarMessage.Item item = inventorySetHotbarMessage.items[index];
                    hotbar.items[index] = item != null ? new Hotbar.Item(item.itemId, item.count, item.instanceId) : null;
                }

                hotbar.limitToInventory(inventory);

                return new EarthDB.Query(true)
                    .Update("hotbar", inventorySetHotbarMessage.playerId, hotbar);
            })
            .Execute(earthDB);
        return true;
    }

    private static RequestWithInstanceId<T>? readRequest<T>(string str)
    {
        try
        {
            RequestWithInstanceId<T>? request = JsonConvert.DeserializeObject<RequestWithInstanceId<T>>(str);
            return request;
        }
        catch (Exception ex)
        {
            Log.Error($"Bad JSON in buildplates event bus request: {ex}");
            return null;
        }
    }

    private static T? readRawRequest<T>(string str)
    {
        try
        {
            T? request = JsonConvert.DeserializeObject<T>(str);
            return request;
        }
        catch (Exception ex)
        {
            Log.Error($"Bad JSON in buildplates event bus request: {ex}");
            return default;
        }
    }

    private sealed record RequestWithInstanceId<T>(
        string instanceId,
        T request
    );
}
