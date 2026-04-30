using ViennaDotNet.Buildplate.Connector.Model;

namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record PlayerConnectedResponse(
    bool Accepted,
    InventoryResponse? InitialInventoryContents
);
