using System.Diagnostics;
using Solace.Common.Utils;

namespace Solace.DB.Models.Global;

public sealed class TemplateBuildplateEF : IEntityWithId<Guid>, IVersionedEntity, IMergeable<TemplateBuildplateEF>
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

    public async Task MergeWith(TemplateBuildplateEF other, ValueMerger merger)
    {
        merger.CurrentUserId = null;
        merger.CurrentUsername = null;

        // same buildplate
        if (Size == other.Size && Offset == other.Offset)
        {
            switch (await merger.PromptMergeConflictAsync(merger.CreateContextForPropertyName($"Template buildplate '{Id}'"), GetInfoString(), other.GetInfoString(), false))
            {
                case MergeAction.KeepCurrent:
                    break;
                case MergeAction.KeepIncoming:
                    {
                        Name = other.Name;
                        Scale = other.Scale;
                        Night = other.Night;
                        ServerDataObjectId = other.ServerDataObjectId;
                        PreviewObjectId = other.PreviewObjectId;
                    }

                    break;
                default:
                    Debug.Fail($"Unexpected value");
                    break;
            }

            return;
        }

        // different buildplate, override
        Name = other.Name;
        Size = other.Size;
        Offset = other.Offset;
        Scale = other.Scale;
        Night = other.Night;
        ServerDataObjectId = other.ServerDataObjectId;
        PreviewObjectId = other.PreviewObjectId;
    }

    private string GetInfoString()
        => $"Name: {Name}, Scale: {Scale}, Night: {Night}";

    public sealed record Legacy(
        string Name,
        int Size,
        int Offset,
        int Scale, // blocks per meter
        bool Night,
        string ServerDataObjectId,
        string PreviewObjectId
    );
}