using System.Text.Json.Serialization;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Player;

public sealed class Buildplates
{
    [JsonInclude, JsonPropertyName("buildplates")]
    public readonly Dictionary<string, Buildplate> _buildplates = [];

    public Buildplates()
    {
        // empty
    }

    public void AddBuildplate(string id, Buildplate buildplate)
        => _buildplates[id] = buildplate;

    public Buildplate? GetBuildplate(string id)
        => _buildplates.GetOrDefault(id, null);

    public sealed record BuildplateEntry(
        string Id,
        Buildplate Buildplate
    );

    public BuildplateEntry[] GetBuildplates()
        => [.. _buildplates.Select(entry => new BuildplateEntry(entry.Key, entry.Value))];

    public sealed class Buildplate
    {
        public int Size { get; init; }

        public int Offset { get; init; }

        public int Scale { get; init; }

        public bool Night { get; init; }

        public long LastModified { get; set; }

        public string ServerDataObjectId { get; set; }

        public string PreviewObjectId { get; set; }

        public Buildplate(int size, int offset, int scale, bool night, long lastModified, string serverDataObjectId, string previewObjectId)
        {
            Size = size;
            Offset = offset;
            Scale = scale;

            Night = night;

            LastModified = lastModified;
            ServerDataObjectId = serverDataObjectId;
            PreviewObjectId = previewObjectId;
        }
    }
}
