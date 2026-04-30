namespace ViennaDotNet.ApiServer.Types;

public sealed record ResourcePackResponse(int Order, int[] ParsedResourcePackVersion, string RelativePath, string ResourcePackVersion, string ResourcePackId);