namespace Solace.DB.Models.Global;

public sealed record TemplateBuildplate(
    string Name,
    int Size,
    int Offset,
    int Scale, // blocks per meter
    bool Night,
    string ServerDataObjectId,
    string PreviewObjectId
);

public sealed class TemplateBuildplateEF : IVersionedEntity
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public required string Name { get; set; }

    public required int Size { get; set; }

    public required int Offset { get; set; }

    public required int Scale { get; set; } // blocks per meter

    public required bool Night { get; set; }

    public required string ServerDataObjectId { get; set; }

    public required string PreviewObjectId { get; set; }
}