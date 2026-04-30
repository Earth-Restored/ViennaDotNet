namespace ViennaDotNet.ApiServer.Types;

public sealed record LocatorResponse(
    Dictionary<string, LocatorResponse.Environment> ServiceEnvironments,
    Dictionary<string, List<string>> SupportedEnvironments
)
{
    public sealed record Environment(string ServiceUri, string CdnUri, string PlayfabTitleId);
}