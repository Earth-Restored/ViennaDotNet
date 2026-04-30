using Solace.Buildplate.Connector.Model;

namespace Solace.Buildplate.Connector.Model;

public sealed record PlayerConnectedResponse(
    bool Accepted,
    InventoryResponse? InitialInventoryContents
);
