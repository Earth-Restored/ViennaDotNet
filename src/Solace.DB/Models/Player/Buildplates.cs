using System.Diagnostics;
using System.Text.Json.Serialization;
using Solace.Common.Utils;

namespace Solace.DB.Models.Player;

public sealed class LegacyBuildplates : IEquatable<LegacyBuildplates>
{
    [JsonInclude, JsonPropertyName("buildplates")]
    public Dictionary<string, Buildplate> Buildplates = [];

    public LegacyBuildplates()
    {
        // empty
    }

    public bool Equals(LegacyBuildplates? other)
        => other is not null &&
        Buildplates.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)).SequenceEqual(other.Buildplates.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)));

    public override bool Equals(object? obj)
        => Equals(obj as LegacyBuildplates);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var item in Buildplates.OrderBy(static item => item.Key, StringComparer.Ordinal))
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
        string? Name,
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

public sealed class BuildplateEF : IEntityWithId<Guid>, IVersionedEntity, IMergeable<BuildplateEF>
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    public Guid? TemplateId { get; set; }

    public required string Name { get; set; }

    public required int Size { get; set; }

    public required int Offset { get; set; }

    public required int Scale { get; set; }

    public required bool Night { get; set; }

    public required long LastModified { get; set; }

    public required string ServerDataObjectId { get; set; }

    public required string PreviewObjectId { get; set; }

    public async Task MergeWith(BuildplateEF other, ValueMerger merger)
    {
        merger.CurrentUserId = Id.ToString();
        merger.CurrentUsername = Account?.Username;

        // same buildplate
        if (AccountId == other.AccountId && TemplateId == other.TemplateId && Size == other.Size && Offset == other.Offset)
        {
            switch (await merger.PromptMergeConflictAsync(merger.CreateContextForPropertyName($"Buildplate '{Id}'"), GetInfoString(), other.GetInfoString(), false))
            {
                case MergeAction.KeepCurrent:
                    break;
                case MergeAction.KeepIncoming:
                    {
                        Name = other.Name;
                        Scale = other.Scale;
                        Night = other.Night;
                        LastModified = other.LastModified;
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
        AccountId = other.AccountId;
        TemplateId = other.TemplateId;
        Name = other.Name;
        Size = other.Size;
        Offset = other.Offset;
        Scale = other.Scale;
        Night = other.Night;
        LastModified = other.LastModified;
        ServerDataObjectId = other.ServerDataObjectId;
        PreviewObjectId = other.PreviewObjectId;
    }

    private string GetInfoString()
        => $"Name: {Name}, Scale: {Scale}, Night: {Night}, Last modified: {DateTimeOffset.FromUnixTimeMilliseconds(LastModified).UtcDateTime:s}";
}