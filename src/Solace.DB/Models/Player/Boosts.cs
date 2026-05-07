namespace Solace.DB.Models.Player;

public sealed class Boosts : IEquatable<Boosts>
{
    public ActiveBoost?[] ActiveBoosts { get; init; }

    public Boosts()
    {
        ActiveBoosts = new ActiveBoost[5];
    }

    public ActiveBoost? Get(string instanceId)
        => ActiveBoosts.FirstOrDefault(activeBoost => activeBoost is not null && activeBoost.InstanceId == instanceId);

    public ActiveBoost[] Prune(long currentTime)
    {
        LinkedList<ActiveBoost> prunedBoosts = [];
        for (int index = 0; index < ActiveBoosts.Length; index++)
        {
            ActiveBoost? activeBoost = ActiveBoosts[index];
            if (activeBoost is not null && activeBoost.StartTime + activeBoost.Duration < currentTime)
            {
                ActiveBoosts[index] = null;
                prunedBoosts.AddLast(activeBoost);
            }
        }

        return [.. prunedBoosts];
    }

    public bool Equals(Boosts? other)
        => other is not null && ActiveBoosts.SequenceEqual(other.ActiveBoosts);

    public override bool Equals(object? obj)
        => Equals(obj as Boosts);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var item in ActiveBoosts)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public sealed record ActiveBoost(
        string InstanceId,
        string ItemId,
        long StartTime,
        long Duration
    );
}

public sealed class BoostsEF : IVersionedEntity
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Account Account { get; set; } = null!;

    public ActiveBoost?[] ActiveBoosts { get; set; } = new ActiveBoost[5];

    public ActiveBoost? Get(string instanceId)
        => ActiveBoosts.FirstOrDefault(activeBoost => activeBoost is not null && activeBoost.InstanceId == instanceId);

    public ActiveBoost[] Prune(long currentTime)
    {
        LinkedList<ActiveBoost> prunedBoosts = [];
        for (int index = 0; index < ActiveBoosts.Length; index++)
        {
            ActiveBoost? activeBoost = ActiveBoosts[index];
            if (activeBoost is not null && activeBoost.StartTime + activeBoost.Duration < currentTime)
            {
                ActiveBoosts[index] = null;
                prunedBoosts.AddLast(activeBoost);
            }
        }

        return [.. prunedBoosts];
    }

    public sealed record ActiveBoost(
        string InstanceId,
        string ItemId,
        long StartTime,
        long Duration
    );
}
