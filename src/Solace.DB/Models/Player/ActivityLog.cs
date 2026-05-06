using System.Text.Json.Serialization;
using Solace.Common.Utils;
using Solace.DB.Models.Common;

namespace Solace.DB.Models.Player;

public sealed class ActivityLog : IEquatable<ActivityLog>
{
    [JsonInclude, JsonPropertyName("entries")]
    public List<Entry> _entries;

    public ActivityLog()
    {
        _entries = [];
    }

    [JsonIgnore]
    public IEnumerable<Entry> Entries => _entries;

    public ActivityLog Copy()
    {
        var activityLog = new ActivityLog();
        activityLog._entries.AddRange(_entries);
        return activityLog;
    }

    public void AddEntry(Entry entry)
        => _entries.Add(entry);

    public void Prune()
    {
        // it is widely known that the activity log is length limited but there is only ONE person who has stated how long it was limited to and apparently it is 40 entries
        if (_entries.Count > 40)
        {
            _entries.RemoveRange(0, _entries.Count - 40);
        }
    }

    public bool Equals(ActivityLog? other)
        => other is not null && _entries.SequenceEqual(other._entries);

    public override string ToString()
        => $"{_entries.Count} entries";

    public override bool Equals(object? obj)
        => Equals(obj as ActivityLog);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var item in _entries)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(LevelUpEntry), "LEVEL_UP")]
    [JsonDerivedType(typeof(TappableEntry), "TAPPABLE")]
    [JsonDerivedType(typeof(JournalItemUnlockedEntry), "JOURNAL_ITEM_UNLOCKED")]
    [JsonDerivedType(typeof(CraftingCompletedEntry), "CRAFTING_COMPLETED")]
    [JsonDerivedType(typeof(SmeltingCompletedEntry), "SMELTING_COMPLETED")]
    [JsonDerivedType(typeof(BoostActivatedEntry), "BOOST_ACTIVATED")]
    public abstract class Entry : IEquatable<Entry>
    {
        public long Timestamp { get; init; }

        [JsonIgnore]
        public TypeE Type { get; init; }

        protected Entry(long timestamp, TypeE type)
        {
            Timestamp = timestamp;
            Type = type;
        }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum TypeE
        {
#pragma warning disable CA1707 // Identifiers should not contain underscores
            LEVEL_UP,
            TAPPABLE,
            JOURNAL_ITEM_UNLOCKED,
            CRAFTING_COMPLETED,
            SMELTING_COMPLETED,
            BOOST_ACTIVATED,
#pragma warning restore CA1707 // Identifiers should not contain underscores
        }

        public abstract bool Equals(Entry? other);

        public override bool Equals(object? obj)
            => Equals(obj as Entry);

        public abstract override int GetHashCode();
    }

    public sealed class LevelUpEntry : Entry
    {
        public int Level { get; init; }

        public LevelUpEntry(long timestamp, int level)
            : base(timestamp, TypeE.LEVEL_UP)
        {
            Level = level;
        }

        public override bool Equals(Entry? other)
            => other is LevelUpEntry levelUp && Timestamp == levelUp.Timestamp && Level == levelUp.Level;

        public override int GetHashCode()
            => HashCode.Combine(Timestamp, Level);
    }

    public sealed class TappableEntry : Entry
    {
        public Rewards Rewards { get; init; }

        public TappableEntry(long timestamp, Rewards rewards)
            : base(timestamp, TypeE.TAPPABLE)
        {
            Rewards = rewards;
        }

        public override bool Equals(Entry? other)
            => other is TappableEntry tappable && Timestamp == tappable.Timestamp && Rewards.Equals(tappable.Rewards);

        public override int GetHashCode()
            => HashCode.Combine(Timestamp, Rewards);
    }

    public sealed class JournalItemUnlockedEntry : Entry
    {
        public string ItemId { get; init; }

        public JournalItemUnlockedEntry(long timestamp, string itemId)
            : base(timestamp, TypeE.JOURNAL_ITEM_UNLOCKED)
        {
            ItemId = itemId;
        }

        public override bool Equals(Entry? other)
            => other is JournalItemUnlockedEntry journalUnlock && Timestamp == journalUnlock.Timestamp && ItemId == journalUnlock.ItemId;

        public override int GetHashCode()
            => HashCode.Combine(Timestamp, ItemId);
    }

    public sealed class CraftingCompletedEntry : Entry
    {
        public Rewards Rewards { get; init; }

        public CraftingCompletedEntry(long timestamp, Rewards rewards)
            : base(timestamp, TypeE.CRAFTING_COMPLETED)
        {
            Rewards = rewards;
        }

        public override bool Equals(Entry? other)
            => other is CraftingCompletedEntry crafting && Timestamp == crafting.Timestamp && Rewards.Equals(crafting.Rewards);

        public override int GetHashCode()
            => HashCode.Combine(Timestamp, Rewards);
    }

    public sealed class SmeltingCompletedEntry : Entry
    {
        public Rewards Rewards { get; init; }

        public SmeltingCompletedEntry(long timestamp, Rewards rewards)
            : base(timestamp, TypeE.SMELTING_COMPLETED)
        {
            Rewards = rewards;
        }

        public override bool Equals(Entry? other)
            => other is SmeltingCompletedEntry smelting && Timestamp == smelting.Timestamp && Rewards.Equals(smelting.Rewards);

        public override int GetHashCode()
            => HashCode.Combine(Timestamp, Rewards);
    }

    public sealed class BoostActivatedEntry : Entry
    {
        public string ItemId { get; init; }

        public BoostActivatedEntry(long timestamp, string itemId)
            : base(timestamp, TypeE.BOOST_ACTIVATED)
        {
            ItemId = itemId;
        }

        public override bool Equals(Entry? other)
            => other is BoostActivatedEntry boost && Timestamp == boost.Timestamp && ItemId == boost.ItemId;

        public override int GetHashCode()
            => HashCode.Combine(Timestamp, ItemId);
    }
}
