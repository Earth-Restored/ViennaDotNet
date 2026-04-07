namespace ViennaDotNet.DB.Models.Global;

public sealed record TemplateBuildplate(
    string Name,
    int Size,
    int Offset,
    int Scale, // blocks per meter
    bool Night,
    string ServerDataObjectId,
    string PreviewObjectId,
    string? LauncherPreviewObjectId
);