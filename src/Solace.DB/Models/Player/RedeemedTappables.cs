using System.Text.Json.Serialization;
using Solace.Common.Utils;

namespace Solace.DB.Models.Player;

public sealed class RedeemedTappablesEF : IEntityWithId<Guid>, IVersionedEntity, IMergeable<RedeemedTappablesEF>
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Account Account { get; set; } = null!;

    public Dictionary<Guid, long> Tappables = [];

    public bool IsRedeemed(Guid id)
        => Tappables.ContainsKey(id);

    public void Add(Guid id, long expiresAt)
        => Tappables[id] = expiresAt;

    public void Prune(long currentTime)
        => Tappables.RemoveIf(entry => entry.Value < currentTime);

    public async Task MergeWith(RedeemedTappablesEF other, ValueMerger merger)
    {
        merger.CurrentUserId = Id.ToString();
        merger.CurrentUsername = Account?.Username;

        foreach (var item in other.Tappables)
        {
            if (!Tappables.TryGetValue(item.Key, out var currentValue))
            {
                Tappables.Add(item.Key, item.Value);
            }
            else
            {
                Tappables[item.Key] = long.Max(currentValue, item.Value);
            }
        }
    }

    public sealed class Legacy : IEquatable<Legacy>
    {
        [JsonInclude, JsonPropertyName("tappables")]
        public Dictionary<string, long> Tappables = [];

        public Legacy()
        {
            // empty
        }

        public bool Equals(Legacy? other)
            => other is not null && Tappables.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)).SequenceEqual(other.Tappables.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)));

        public override bool Equals(object? obj)
            => Equals(obj as Legacy);

        public override int GetHashCode()
        {
            var hash = new HashCode();

            foreach (var item in Tappables.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                hash.Add(item.Key);
                hash.Add(item.Value);
            }

            return hash.ToHashCode();
        }
    }
}