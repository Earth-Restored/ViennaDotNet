using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Player;

public sealed class Boosts
{
    [JsonProperty]
    private readonly Dictionary<string, ActiveBoost> activeBoosts = [];

    public Boosts()
    {
        // empty
    }

    public ActiveBoost[] getAll()
    {
        return [.. activeBoosts.Values];
    }

    public ActiveBoost? get(string instanceId)
    {
        return activeBoosts.GetValueOrDefault(instanceId);
    }

    public void add(string instanceId, string itemId, long startTime, long duration)
    {
        activeBoosts[instanceId] = new ActiveBoost(instanceId, itemId, startTime, duration);
    }

    public void remove(string instanceId)
    {
        activeBoosts.Remove(instanceId);
    }

    public void prune(long currentTime)
    {
        activeBoosts.RemoveIf(item => item.Value.startTime + item.Value.duration < currentTime);
    }

    public sealed record ActiveBoost(
        [property: JsonProperty] string instanceId,
        [property: JsonProperty] string itemId,
        [property: JsonProperty] long startTime,
        [property: JsonProperty] long duration
    );
}
