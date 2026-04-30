namespace Solace.ApiServer.Types.Common;

public sealed record BurnRate(
    int BurnTime,
    int HeatPerSecond
);
