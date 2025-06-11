namespace ViennaDotNet.ApiServer.Types.Common;

public sealed record Effect(
    string type,
    string? duration, 
    int? value,
    string? unit,
    string targets,
    string[] items,
    string[] itemScenarios,
    string activation,
    string? modifiesType
);