namespace ViennaDotNet.DB.Models.Player;

public sealed class Boosts
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

    public sealed record ActiveBoost(
        string InstanceId,
        string ItemId,
        long StartTime,
        long Duration
    );
}
