using System.Text.Json.Serialization;
using Solace.ApiServer.Types.Common;
using static Solace.ApiServer.Types.Journal.JournalRecord;

namespace Solace.ApiServer.Types.Journal;

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
#pragma warning disable CA1707 // Identifiers should not contain underscores
            [JsonStringEnumMemberName("LevelUp")] LEVEL_UP,
            [JsonStringEnumMemberName("TappableCollected")] TAPPABLE,
            [JsonStringEnumMemberName("JournalContentCollected")] JOURNAL_ITEM_UNLOCKED,
            [JsonStringEnumMemberName("CraftingJobCompleted")] CRAFTING_COMPLETED,
            [JsonStringEnumMemberName("SmeltingJobCompleted")] SMELTING_COMPLETED,
            [JsonStringEnumMemberName("BoostActivated")] BOOST_ACTIVATED,
#pragma warning restore CA1707 // Identifiers should not contain underscores
        }
    }
}
