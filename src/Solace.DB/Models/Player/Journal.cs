using System.Text.Json.Serialization;
using Solace.Common.Utils;

namespace Solace.DB.Models.Player;

public sealed class Journal
{
    [JsonInclude, JsonPropertyName("items")]
    public Dictionary<string, ItemJournalEntry> _items;

    public Journal()
    {
        _items = [];
    }

    [JsonIgnore]
    public Dictionary<string, ItemJournalEntry> Items => _items;

    public Journal Copy()
    {
        var journal = new Journal();
        journal._items.AddRange(_items);
        return journal;
    }

    public ItemJournalEntry? GetItem(string uuid)
        => _items.GetValueOrDefault(uuid);

    public int AddCollectedItem(string uuid, long timestamp, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        ItemJournalEntry? itemJournalEntry = _items.GetOrDefault(uuid, null);
        if (itemJournalEntry is null)
        {
            _items[uuid] = new ItemJournalEntry(timestamp, timestamp, count);
            return 0;
        }
        else
        {
            _items[uuid] = new ItemJournalEntry(itemJournalEntry.FirstSeen, itemJournalEntry.LastSeen, itemJournalEntry.AmountCollected + count);
            return itemJournalEntry.AmountCollected;
        }
    }

    public sealed record ItemJournalEntry(
        long FirstSeen,
        long LastSeen,
        int AmountCollected
    );
}
