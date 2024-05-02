using Newtonsoft.Json;
using Serilog;
using System;
using System.Buffers.Text;
using System.Text;
using ViennaDotNet.ApiServer.Types.Catalog;
using ViennaDotNet.Buildplate.Connector.Model;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Common;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.ApiServer.Utils
{
    public sealed class BuildplateInstanceRequestHandler
    {
        public static void start(EarthDB earthDB, EventBusClient eventBusClient, ObjectStoreClient objectStoreClient, Catalog catalog)
        {
            new BuildplateInstanceRequestHandler(earthDB, eventBusClient, objectStoreClient, catalog);
        }

        private readonly EarthDB earthDB;
        private readonly ObjectStoreClient objectStoreClient;
        private readonly Catalog catalog;
        private readonly BuildplateInstancesManager buildplateInstancesManager;

        public BuildplateInstanceRequestHandler(EarthDB earthDB, EventBusClient eventBusClient, ObjectStoreClient objectStoreClient, Catalog catalog)
        {
            this.earthDB = earthDB;
            this.objectStoreClient = objectStoreClient;
            this.catalog = catalog;
            this.buildplateInstancesManager = new BuildplateInstancesManager(eventBusClient);    // TODO: would be nicer to use the same instance as BuildplatesRouter

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
                                    if (buildplateLoadRequest == null)
                                        return null;

                                    BuildplateLoadResponse? buildplateLoadResponse = handleLoad(buildplateLoadRequest.playerId, buildplateLoadRequest.buildplateId);
                                    return buildplateLoadResponse != null ? JsonConvert.SerializeObject(buildplateLoadResponse) : null;
                                }
                            case "saved":
                                {
                                    RequestWithBuildplateId<WorldSavedMessage>? requestWithBuildplateId = readRequest<WorldSavedMessage>(request.data);
                                    if (requestWithBuildplateId == null)
                                        return null;

                                    return handleSaved(requestWithBuildplateId.playerId, requestWithBuildplateId.buildplateId, requestWithBuildplateId.instanceId, requestWithBuildplateId.request.dataBase64, request.timestamp) ? "" : null;
                                }
                            case "playerConnected":
                                {
                                    Log.Debug("RequestHandler playerConnected");
                                    RequestWithBuildplateId<PlayerConnectedRequest>? requestWithBuildplateId = readRequest<PlayerConnectedRequest>(request.data);
                                    if (requestWithBuildplateId == null)
                                        return null;

                                    PlayerConnectedResponse? playerConnectedResponse = handlePlayerConnected(requestWithBuildplateId.playerId, requestWithBuildplateId.buildplateId, requestWithBuildplateId.instanceId, requestWithBuildplateId.request);
                                    return playerConnectedResponse != null ? JsonConvert.SerializeObject(playerConnectedResponse) : null;
                                }
                            case "playerDisconnected":
                                {
                                    RequestWithBuildplateId<PlayerDisconnectedRequest>? requestWithBuildplateId = readRequest<PlayerDisconnectedRequest>(request.data);
                                    if (requestWithBuildplateId == null)
                                        return null;

                                    PlayerDisconnectedResponse? playerDisconnectedResponse = handlePlayerDisconnected(requestWithBuildplateId.playerId, requestWithBuildplateId.buildplateId, requestWithBuildplateId.instanceId, requestWithBuildplateId.request);
                                    return playerDisconnectedResponse != null ? JsonConvert.SerializeObject(playerDisconnectedResponse) : null;
                                }
                            case "inventoryAdd":
                                {
                                    RequestWithBuildplateId<InventoryAddItemMessage>? requestWithBuildplateId = readRequest<InventoryAddItemMessage>(request.data);
                                    if (requestWithBuildplateId == null)
                                        return null;

                                    return handleInventoryAdd(requestWithBuildplateId.playerId, requestWithBuildplateId.buildplateId, requestWithBuildplateId.instanceId, requestWithBuildplateId.request, request.timestamp) ? "" : null;
                                }
                            case "inventoryRemove":
                                {
                                    RequestWithBuildplateId<InventoryRemoveItemMessage>? requestWithBuildplateId = readRequest<InventoryRemoveItemMessage>(request.data);
                                    if (requestWithBuildplateId == null)
                                        return null;

                                    return handleInventoryRemove(requestWithBuildplateId.playerId, requestWithBuildplateId.buildplateId, requestWithBuildplateId.instanceId, requestWithBuildplateId.request) ? "" : null;
                                }
                            case "inventoryUpdateWear":
                                {
                                    RequestWithBuildplateId<InventoryUpdateItemWearMessage>? requestWithBuildplateId = readRequest<InventoryUpdateItemWearMessage>(request.data);
                                    if (requestWithBuildplateId == null)
                                        return null;

                                    return handleInventoryUpdateWear(requestWithBuildplateId.playerId, requestWithBuildplateId.buildplateId, requestWithBuildplateId.instanceId, requestWithBuildplateId.request) ? "" : null;
                                }
                            case "inventorySetHotbar":
                                {
                                    RequestWithBuildplateId<InventorySetHotbarMessage>? requestWithBuildplateId = readRequest<InventorySetHotbarMessage>(request.data);
                                    if (requestWithBuildplateId == null)
                                        return null;

                                    return handleInventorySetHotbar(requestWithBuildplateId.playerId, requestWithBuildplateId.buildplateId, requestWithBuildplateId.instanceId, requestWithBuildplateId.request) ? "" : null;
                                }
                            default:
                                return null;
                        }
                    }
                    catch (EarthDB.DatabaseException exception)
                    {
                        Log.Error($"Database error while handling request: {exception}");
                        return null;
                    }
                },
                () =>
                {
                    Log.Fatal("Buildplates event bus request handler error");
                    Environment.Exit(1);
                }
            ));
        }

        private record BuildplateLoadRequest(
            string playerId,
            string buildplateId
        )
        {
        }

        private record BuildplateLoadResponse(
            string serverDataBase64
        )
        {
        }

        private BuildplateLoadResponse? handleLoad(string playerId, string buildplateId)
        {
            EarthDB.Results results = new EarthDB.Query(false)
                .Get("buildplates", playerId, typeof(Buildplates))
                .Execute(earthDB);
            Buildplates buildplates = (Buildplates)results.Get("buildplates").Value;

            Buildplates.Buildplate? buildplate = buildplates.getBuildplate(buildplateId);
            if (buildplate == null)
                return null;

            byte[]? serverData = (byte[]?)objectStoreClient.get(buildplate.serverDataObjectId).Task.Result;
            if (serverData == null)
            {
                Log.Error($"Data object {buildplate.serverDataObjectId} for buildplate {buildplateId} could not be loaded from object store");
                return null;
            }
            string serverDataBase64 = Convert.ToBase64String(serverData);

            return new BuildplateLoadResponse(serverDataBase64);
        }

        private bool handleSaved(string playerId, string buildplateId, string instanceId, string dataBase64, long timestamp)
        {
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

            string? serverDataObjectId = (string?)objectStoreClient.store(serverData).Task.Result;
            if (serverDataObjectId == null)
            {
                Log.Error($"Could not store new data object for buildplate {buildplateId} in object store");
                return false;
            }

            string? previewObjectId;
            if (preview != null)
            {
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
                            return new EarthDB.Query(false)
                                .Extra("exists", false);
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
                        objectStoreClient.delete(previewObjectId);
                    return false;
                }
            }
            catch (EarthDB.DatabaseException)
            {
                objectStoreClient.delete(serverDataObjectId);
                if (previewObjectId != null)
                    objectStoreClient.delete(previewObjectId);

                throw;
            }
        }

        private PlayerConnectedResponse? handlePlayerConnected(string playerId, string buildplateId, string instanceId, PlayerConnectedRequest playerConnectedRequest)
        {
            // TODO: check join code etc.

            EarthDB.Results results = new EarthDB.Query(false)
                .Get("inventory", playerConnectedRequest.uuid, typeof(Inventory))
                .Get("hotbar", playerConnectedRequest.uuid, typeof(Hotbar))
                .Execute(earthDB);
            Inventory inventory = (Inventory)results.Get("inventory").Value;
            Hotbar hotbar = (Hotbar)results.Get("hotbar").Value;

            PlayerConnectedResponse? playerConnectedResponse = new PlayerConnectedResponse(
                true,
                new PlayerConnectedResponse.Inventory(
                    inventory.getStackableItems()
                    .Select(item=> new PlayerConnectedResponse.Inventory.Item(item.id, item.count ?? 0, null, 0))
                    .Concat(
                        inventory.getNonStackableItems()
                        .SelectMany((item)=>item.instances
                            .Select(instance=> new PlayerConnectedResponse.Inventory.Item(item.id, 1, instance.instanceId, instance.wear))
                        )
                    ).Where(item=>item.count > 0).ToArray(),
                    hotbar.items.Select(item=>item != null && item.count > 0 ? new PlayerConnectedResponse.Inventory.HotbarItem(item.uuid, item.count, item.instanceId) : null).ToArray()
                )
            );

            return playerConnectedResponse;
        }

        private PlayerDisconnectedResponse? handlePlayerDisconnected(string playerId, string buildplateId, string instanceId, PlayerDisconnectedRequest playerDisconnectedRequest)
        {
            // TODO

            return new PlayerDisconnectedResponse();
        }

        private bool handleInventoryAdd(string playerId, string buildplateId, string instanceId, InventoryAddItemMessage inventoryAddItemMessage, long timestamp)
        {
            ItemsCatalog.Item? catalogItem = catalog.itemsCatalog.items.Where(item => item.id == inventoryAddItemMessage.itemId).FirstOrDefault();
            if (catalogItem == null)
                return false;
            if (!catalogItem.stacks && inventoryAddItemMessage.instanceId == null)
                return false;

            EarthDB.Results results = new EarthDB.Query(true)
                .Get("inventory", inventoryAddItemMessage.playerId, typeof(Inventory))
                .Get("journal", inventoryAddItemMessage.playerId, typeof(Journal))
                .Then(results1 =>
                {
                    Inventory inventory = (Inventory)results1.Get("inventory").Value;
                    Journal journal = (Journal)results1.Get("journal").Value;

                    if (catalogItem.stacks)
                        inventory.addItems(inventoryAddItemMessage.itemId, inventoryAddItemMessage.count);
                    else
                        inventory.addItems(inventoryAddItemMessage.itemId, new NonStackableItemInstance[] { new NonStackableItemInstance(inventoryAddItemMessage.instanceId!, inventoryAddItemMessage.wear) });

                    journal.touchItem(inventoryAddItemMessage.itemId, timestamp);

                    return new EarthDB.Query(true)
                        .Update("inventory", inventoryAddItemMessage.playerId, inventory)
                        .Update("journal", inventoryAddItemMessage.playerId, journal);
                })
                .Execute(earthDB);
            return true;
        }

        private bool handleInventoryRemove(string playerId, string buildplateId, string instanceId, InventoryRemoveItemMessage inventoryRemoveItemMessage)
        {
            EarthDB.Results results = new EarthDB.Query(true)
            .Get("inventory", inventoryRemoveItemMessage.playerId, typeof(Inventory))
                .Get("hotbar", inventoryRemoveItemMessage.playerId, typeof(Hotbar))
                .Then(results1 =>
                {
                    Inventory inventory = (Inventory)results1.Get("inventory").Value;
                    Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                    if (inventoryRemoveItemMessage.instanceId != null)
                    {
                        if (inventory.takeItems(inventoryRemoveItemMessage.itemId, new string[] { inventoryRemoveItemMessage.instanceId }) == null)
                            Log.Warning($"Buildplate instance {instanceId} attempted to remove item {inventoryRemoveItemMessage.itemId} {inventoryRemoveItemMessage.instanceId} from player {inventoryRemoveItemMessage.playerId} that is not in inventory");
                    }
                    else
                    {
                        if (!inventory.takeItems(inventoryRemoveItemMessage.itemId, inventoryRemoveItemMessage.count))
                        {
                            int count = inventory.getItemCount(inventoryRemoveItemMessage.itemId);
                            if (!inventory.takeItems(inventoryRemoveItemMessage.itemId, count))
                                count = 0;

                            Log.Warning($"Buildplate instance {instanceId} attempted to remove item {inventoryRemoveItemMessage.itemId} {inventoryRemoveItemMessage.count - count} from player {inventoryRemoveItemMessage.playerId} that is not in inventory");
                        }
                    }

                    hotbar.limitToInventory(inventory);

                    return new EarthDB.Query(true)
                        .Update("inventory", inventoryRemoveItemMessage.playerId, inventory)
                        .Update("hotbar", inventoryRemoveItemMessage.playerId, hotbar);
                })
                .Execute(earthDB);
            return true;
        }

        private bool handleInventoryUpdateWear(string playerId, string buildplateId, string instanceId, InventoryUpdateItemWearMessage inventoryUpdateItemWearMessage)
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
                        if (inventory.takeItems(inventoryUpdateItemWearMessage.itemId, new string[] { inventoryUpdateItemWearMessage.instanceId }) == null)
                            throw new InvalidOperationException();

                        inventory.addItems(inventoryUpdateItemWearMessage.itemId, new NonStackableItemInstance[] { new NonStackableItemInstance(inventoryUpdateItemWearMessage.instanceId, inventoryUpdateItemWearMessage.wear) });
                    }
                    else
                        Log.Warning("Buildplate instance {instanceId} attempted to update item wear for item {inventoryUpdateItemWearMessage.itemId()} {inventoryUpdateItemWearMessage.instanceId()} player {inventoryUpdateItemWearMessage.playerId()} that is not in inventory");

                    return new EarthDB.Query(true)
                        .Update("inventory", inventoryUpdateItemWearMessage.playerId, inventory);
                })
                .Execute(earthDB);
            return true;
        }

        private bool handleInventorySetHotbar(string playerId, string buildplateId, string instanceId, InventorySetHotbarMessage inventorySetHotbarMessage)
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

        private static RequestWithBuildplateId<T>? readRequest<T>(string str)
        {
            try
            {
                RequestWithBuildplateId<T>? request = JsonConvert.DeserializeObject<RequestWithBuildplateId<T>>(str);
                return request;
            }
            catch (Exception exception)
            {
                Log.Error($"Bad JSON in buildplates event bus request: {exception}");
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
            catch (Exception exception)
            {
                Log.Error($"Bad JSON in buildplates event bus request: {exception}");
                return default;
            }
        }

        private record RequestWithBuildplateId<T>(
            string playerId,
            string buildplateId,
            string instanceId,
            T request
        )
        {
        }
    }
}
