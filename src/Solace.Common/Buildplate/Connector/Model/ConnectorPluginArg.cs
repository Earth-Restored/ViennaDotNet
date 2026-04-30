namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record ConnectorPluginArg(
    string EventBusAddress,
    string EventBusQueueName,
    InventoryType InventoryType
);
