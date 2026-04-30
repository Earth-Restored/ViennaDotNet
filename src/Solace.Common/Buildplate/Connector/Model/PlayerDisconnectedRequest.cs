using Solace.Buildplate.Connector.Model;

namespace Solace.Buildplate.Connector.Model;

public sealed record PlayerDisconnectedRequest(
     string PlayerId,
     InventoryResponse? BackpackContents
);
