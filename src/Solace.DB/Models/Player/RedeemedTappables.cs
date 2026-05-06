using System.Text.Json.Serialization;
using Solace.Common.Utils;

namespace Solace.DB.Models.Player;

public sealed class RedeemedTappables : IEquatable<RedeemedTappables>
{
    [JsonInclude, JsonPropertyName("tappables")]
    public Dictionary<string, long> _tappables = [];

    public RedeemedTappables()
    {
        // empty
    }

    public bool IsRedeemed(string id)
        => _tappables.ContainsKey(id);

    public void Add(string id, long expiresAt)
        => _tappables[id] = expiresAt;

    public void Prune(long currentTime)
        => _tappables.RemoveIf(entry => entry.Value < currentTime);

    public bool Equals(RedeemedTappables? other)
        => other is not null && _tappables.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)).SequenceEqual(other._tappables.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)));

    public override bool Equals(object? obj)
        => Equals(obj as RedeemedTappables);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var item in _tappables.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            hash.Add(item.Key);
            hash.Add(item.Value);
        }

        return hash.ToHashCode();
    }
}
