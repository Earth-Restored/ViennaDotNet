using System.Text.Json.Serialization;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Common;

namespace ViennaDotNet.DB.Models.Player;

public sealed class ActivityLog
{
    [JsonInclude, JsonPropertyName("entries")]
    public LinkedList<Entry> _entries;

    public ActivityLog()
    {
        _entries = new();
    }

    [JsonIgnore]
    public IEnumerable<Entry> Entries => _entries;

    public ActivityLog Copy()
    {
        ActivityLog activityLog = new ActivityLog();
        activityLog._entries.AddRange(_entries);
        return activityLog;
    }

    public void AddEntry(Entry entry)
        => _entries.AddLast(entry);

    public void Prune()
    {
        // it is widely known that the activity log is length limited but there is only ONE person who has stated how long it was limited to and apparently it is 40 entries
        while (_entries.Count > 40)
        {
            _entries.RemoveFirst();
        }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(LevelUpEntry), "LEVEL_UP")]
    [JsonDerivedType(typeof(TappableEntry), "TAPPABLE")]
    [JsonDerivedType(typeof(JournalItemUnlockedEntry), "JOURNAL_ITEM_UNLOCKED")]
    [JsonDerivedType(typeof(CraftingCompletedEntry), "CRAFTING_COMPLETED")]
    [JsonDerivedType(typeof(SmeltingCompletedEntry), "SMELTING_COMPLETED")]
    [JsonDerivedType(typeof(BoostActivatedEntry), "BOOST_ACTIVATED")]
    public abstract class Entry
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
            LEVEL_UP,
            TAPPABLE,
            JOURNAL_ITEM_UNLOCKED,
            CRAFTING_COMPLETED,
            SMELTING_COMPLETED,
            BOOST_ACTIVATED,
        }
    }

    public sealed class LevelUpEntry : Entry
    {
        public int Level { get; init; }

        public LevelUpEntry(long timestamp, int level)
            : base(timestamp, TypeE.LEVEL_UP)
        {
            Level = level;
        }
    }

    public sealed class TappableEntry : Entry
    {
        public Rewards Rewards { get; init; }

        public TappableEntry(long timestamp, Rewards rewards)
            : base(timestamp, TypeE.TAPPABLE)
        {
            Rewards = rewards;
        }
    }

    public sealed class JournalItemUnlockedEntry : Entry
    {
        public string ItemId { get; init; }

        public JournalItemUnlockedEntry(long timestamp, string itemId)
            : base(timestamp, TypeE.JOURNAL_ITEM_UNLOCKED)
        {
            ItemId = itemId;
        }
    }

    public sealed class CraftingCompletedEntry : Entry
    {
        public Rewards Rewards { get; init; }

        public CraftingCompletedEntry(long timestamp, Rewards rewards)
            : base(timestamp, TypeE.CRAFTING_COMPLETED)
        {
            Rewards = rewards;
        }
    }

    public sealed class SmeltingCompletedEntry : Entry
    {
        public Rewards Rewards { get; init; }

        public SmeltingCompletedEntry(long timestamp, Rewards rewards)
            : base(timestamp, TypeE.SMELTING_COMPLETED)
        {
            Rewards = rewards;
        }
    }

    public sealed class BoostActivatedEntry : Entry
    {
        public string ItemId { get; init; }

        public BoostActivatedEntry(long timestamp, string itemId)
            : base(timestamp, TypeE.BOOST_ACTIVATED)
        {
            ItemId = itemId;
        }
    }
}
