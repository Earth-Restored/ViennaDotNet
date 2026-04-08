using System.Text.Json.Serialization;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Player;

public sealed class Buildplates
{
    [JsonInclude, JsonPropertyName("buildplates")]
    public Dictionary<string, Buildplate> _buildplates = [];

    public Buildplates()
    {
        // empty
    }

    public void AddBuildplate(string id, Buildplate buildplate)
        => _buildplates[id] = buildplate;

    public Buildplate? GetBuildplate(string id)
        => _buildplates.GetOrDefault(id, null);

    public bool RemoveBuildplate(string id)
        => _buildplates.Remove(id);

    public sealed record BuildplateEntry(
        string Id,
        Buildplate Buildplate
    );

    public IEnumerable<BuildplateEntry> GetBuildplates()
        => _buildplates.Select(entry => new BuildplateEntry(entry.Key, entry.Value));

    public sealed record Buildplate(
        string TemplateId,
        string Name,
        int Size,
        int Offset,
        int Scale,
        bool Night,
        long LastModified,
        string ServerDataObjectId,
        string PreviewObjectId,
        string? LauncherPreviewObjectId
    );
}
