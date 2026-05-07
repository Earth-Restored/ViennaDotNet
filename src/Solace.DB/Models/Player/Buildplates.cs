using System.Text.Json.Serialization;
using Solace.Common.Utils;

namespace Solace.DB.Models.Player;

public sealed class Buildplates : IEquatable<Buildplates>
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

    public bool Equals(Buildplates? other)
        => other is not null &&
        _buildplates.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)).SequenceEqual(other._buildplates.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)));

    public override bool Equals(object? obj)
        => Equals(obj as Buildplates);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var item in _buildplates.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            hash.Add(item.Key);
            hash.Add(item.Value);
        }

        return hash.ToHashCode();
    }

    public sealed class Vienna : IEquatable<Vienna>
    {
        [JsonInclude, JsonPropertyName("buildplates")]
        public Dictionary<string, Buildplate.Vienna> _buildplates = [];

        public Vienna()
        {
            // empty
        }

        public bool Equals(Vienna? other)
            => other is not null &&
            _buildplates.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)).SequenceEqual(other._buildplates.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)));

        public override bool Equals(object? obj)
            => Equals(obj as Vienna);

        public override int GetHashCode()
        {
            var hash = new HashCode();

            foreach (var item in _buildplates.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                hash.Add(item.Key);
                hash.Add(item.Value);
            }

            return hash.ToHashCode();
        }
    }

    public sealed record Buildplate(
        string? TemplateId,
        string Name,
        int Size,
        int Offset,
        int Scale,
        bool Night,
        long LastModified,
        string ServerDataObjectId,
        string PreviewObjectId
    )
    {
        public sealed record Vienna(
            int Size,
            int Offset,
            int Scale,
            bool Night,
            long LastModified,
            string ServerDataObjectId,
            string PreviewObjectId
        );
    }
}

public sealed class BuildplateEF : IVersionedEntity
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    public string? TemplateId { get; set; }

    public required string Name { get; set; }

    public required int Size { get; set; }

    public required int Offset { get; set; }

    public required int Scale { get; set; }

    public required bool Night { get; set; }

    public required long LastModified { get; set; }

    public required string ServerDataObjectId { get; set; }

    public required string PreviewObjectId { get; set; }
}