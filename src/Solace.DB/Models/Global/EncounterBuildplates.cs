using System.Text.Json.Serialization;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Global;

public sealed class EncounterBuildplates
{
    [JsonInclude, JsonPropertyName("encounterBuildplates")]
    public Dictionary<string, EncounterBuildplate> _encounterBuildplates = [];

    public EncounterBuildplates()
    {
    }

    public EncounterBuildplate? GetEncounterBuildplate(string id)
        => _encounterBuildplates.GetOrDefault(id);

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
