namespace ViennaDotNet.Buildplate.Connector.Model;

public sealed record PlayerConnectedRequest(
    string Uuid,
    string JoinCode
);