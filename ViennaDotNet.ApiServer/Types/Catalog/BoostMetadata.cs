using ViennaDotNet.ApiServer.Types.Common;

namespace ViennaDotNet.ApiServer.Types.Catalog;

public record BoostMetadata(
    string name,
    string type,
    string attribute,
    bool canBeDeactivated,
    bool canBeRemoved,
    string? activeDuration,
    bool additive,
    int? level,
    Effect[] effects,
    string? scenario,
    string? cooldown
);
