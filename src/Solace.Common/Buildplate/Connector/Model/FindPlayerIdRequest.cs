namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record FindPlayerIdRequest(
    string MinecraftId,
    string MinecraftName
);