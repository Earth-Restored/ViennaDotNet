using System.Text.Json.Serialization;
using Solace.Common.Utils;

namespace Solace.DB.Models.Player;

public sealed class RedeemedTappables
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
}
