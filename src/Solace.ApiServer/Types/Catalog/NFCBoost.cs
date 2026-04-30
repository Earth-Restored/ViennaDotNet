using Solace.ApiServer.Types.Common;

namespace Solace.ApiServer.Types.Catalog;

public sealed record NFCBoost(
    string Id,
    string Name,
    string Type,
    Rewards Rewards,
    BoostMetadata BoostMetadata,
    bool Deprecated,
    string ToolsVersion
);
