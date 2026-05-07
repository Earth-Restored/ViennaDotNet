using System.Text.Json.Serialization;
using Solace.Common.Utils;

namespace Solace.DB.Models.Player;

public sealed class Journal : IEquatable<Journal>
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

    // KVP is not equatable
    public bool Equals(Journal? other)
        => other is not null && _items.Select(item => (Key: item.Key, Value: item.Value)).OrderBy(item => item.Key, StringComparer.Ordinal).SequenceEqual(other._items.Select(item => (Key: item.Key, Value: item.Value)).OrderBy(item => item.Key, StringComparer.Ordinal));

    public override bool Equals(object? obj)
        => Equals(obj as Journal);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var item in _items.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            hash.Add(item.Key, StringComparer.Ordinal);
            hash.Add(item.Value);
        }

        return hash.ToHashCode();
    }

    public sealed record ItemJournalEntry(
        long FirstSeen,
        long LastSeen,
        int AmountCollected
    );
}

public sealed class JournalEF : IVersionedEntity
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Account Account { get; set; } = null!;

    public Dictionary<string, ItemJournalEntry> Items { get; set; } = [];

    public ItemJournalEntry? GetItem(string uuid)
        => Items.GetValueOrDefault(uuid);

    public int AddCollectedItem(string uuid, long timestamp, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        ItemJournalEntry? itemJournalEntry = Items.GetOrDefault(uuid, null);
        if (itemJournalEntry is null)
        {
            Items[uuid] = new ItemJournalEntry(timestamp, timestamp, count);
            return 0;
        }
        else
        {
            Items[uuid] = new ItemJournalEntry(itemJournalEntry.FirstSeen, itemJournalEntry.LastSeen, itemJournalEntry.AmountCollected + count);
            return itemJournalEntry.AmountCollected;
        }
    }

    public sealed record ItemJournalEntry(
        long FirstSeen,
        long LastSeen,
        int AmountCollected
    );
}