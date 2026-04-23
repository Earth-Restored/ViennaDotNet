namespace ViennaDotNet.ApiServer.Types.Common;

public sealed record Effect(
    string Type,
    string? Duration,
    float? Value,
    string? Unit,
    string Targets,
    string[] Items,
    string[] ItemScenarios,
    string Activation,
    string? ModifiesType
);