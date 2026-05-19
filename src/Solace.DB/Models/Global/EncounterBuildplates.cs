using System.Diagnostics;
using System.Text.Json.Serialization;
using Solace.Common.Utils;

#pragma warning disable CA1716
namespace Solace.DB.Models.Global;
#pragma warning restore CA1716

public sealed class LegacyEncounterBuildplates
{
    [JsonInclude, JsonPropertyName("encounterBuildplates")]
    public Dictionary<string, EncounterBuildplate> EncounterBuildplates = [];

    public LegacyEncounterBuildplates()
    {
    }

    public sealed class EncounterBuildplate
    {
        public int Size { get; }
        public int Offset { get; }
        public int Scale { get; }

        public string ServerDataObjectId { get; }

        public EncounterBuildplate(int size, int offset, int scale, string serverDataObjectId)
        {
            Size = size;
            Offset = offset;
            Scale = scale;

            ServerDataObjectId = serverDataObjectId;
        }
    }
}

public sealed class EncounterBuildplateEF : IEntityWithId<Guid>, IMergeable<EncounterBuildplateEF>
{
    public Guid Id { get; set; }

    public required int Size { get; set; }

    public required int Offset { get; set; }

    public required int Scale { get; set; }

    public required string ServerDataObjectId { get; set; }

    public async Task MergeWith(EncounterBuildplateEF other, ValueMerger merger)
    {
        merger.CurrentUserId = null;
        merger.CurrentUsername = null;

        // same buildplate
        if (Size == other.Size && Offset == other.Offset)
        {
            switch (await merger.PromptMergeConflictAsync(merger.CreateContextForPropertyName($"Encounter buildplate '{Id}'"), GetInfoString(), other.GetInfoString(), false))
            {
                case MergeAction.KeepCurrent:
                    break;
                case MergeAction.KeepIncoming:
                    {
                        Scale = other.Scale;
                        ServerDataObjectId = other.ServerDataObjectId;
                    }

                    break;
                default:
                    Debug.Fail($"Unexpected value");
                    break;
            }

            return;
        }

        // different buildplate, override
        Size = other.Size;
        Offset = other.Offset;
        Scale = other.Scale;
        ServerDataObjectId = other.ServerDataObjectId;
    }

    private string GetInfoString()
        => $"Scale: {Scale}";
}
