using Newtonsoft.Json;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Player;

[JsonObject(MemberSerialization.OptIn)]
public sealed class Journal
{
    [JsonProperty]
    private Dictionary<string, ItemJournalEntry> items;

    public Journal()
    {
        items = [];
    }

    public Journal copy()
    {
        Journal journal = new Journal();
        journal.items.AddRange(items);
        return journal;
    }

    public Dictionary<string, ItemJournalEntry> getItems()
        => new(items);

    public ItemJournalEntry? getItem(string uuid)
        => items.GetValueOrDefault(uuid);

    public int addCollectedItem(string uuid, long timestamp, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        ItemJournalEntry? itemJournalEntry = items.GetOrDefault(uuid, null);
        if (itemJournalEntry is null)
        {
            items[uuid] = new ItemJournalEntry(timestamp, timestamp, count);
            return 0;
        }
        else
        {
            items[uuid] = new ItemJournalEntry(itemJournalEntry.firstSeen, itemJournalEntry.lastSeen, itemJournalEntry.amountCollected + count);
            return itemJournalEntry.amountCollected;
        }
    }

    public record ItemJournalEntry(
        long firstSeen,
        long lastSeen,
        int amountCollected
    )
    {
    }
}
