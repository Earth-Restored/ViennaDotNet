using System.Text.Json.Serialization;
using ViennaDotNet.ApiServer.Types.Common;
using static ViennaDotNet.ApiServer.Types.Journal.JournalRecord;

namespace ViennaDotNet.ApiServer.Types.Journal;

public sealed record JournalRecord(
     Dictionary<string, InventoryJournalEntry> InventoryJournal,
     ActivityLogEntry[] ActivityLog
)
{
    public sealed record InventoryJournalEntry(
        string FirstSeen,
        string LastSeen,
        int AmountCollected
    );

    public sealed record ActivityLogEntry(
        ActivityLogEntry.Type Scenario,
        string EventTime,
        Rewards Rewards,
        Dictionary<string, string> Properties
    )
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum Type
        {
            [JsonStringEnumMemberName("LevelUp")] LEVEL_UP,
            [JsonStringEnumMemberName("TappableCollected")] TAPPABLE,
            [JsonStringEnumMemberName("JournalContentCollected")] JOURNAL_ITEM_UNLOCKED,
            [JsonStringEnumMemberName("CraftingJobCompleted")] CRAFTING_COMPLETED,
            [JsonStringEnumMemberName("SmeltingJobCompleted")] SMELTING_COMPLETED,
            [JsonStringEnumMemberName("BoostActivated")] BOOST_ACTIVATED,
        }
    }
}
