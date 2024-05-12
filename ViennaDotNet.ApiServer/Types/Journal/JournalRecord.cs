using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;
using ViennaDotNet.ApiServer.Types.Common;
using static ViennaDotNet.ApiServer.Types.Journal.JournalRecord;

namespace ViennaDotNet.ApiServer.Types.Journal
{
    public record JournalRecord(
         Dictionary<string, InventoryJournalEntry> inventoryJournal,
         ActivityLogEntry[] activityLog
    )
    {
        public record InventoryJournalEntry(
            string firstSeen,
            string lastSeen,
            int amountCollected
        )
        {
        }

        public record ActivityLogEntry(
            ActivityLogEntry.Type scenario,
            string eventTime,
            Rewards rewards,
            Dictionary<string, string> properties
        )
        {
            [JsonConverter(typeof(StringEnumConverter))]
            public enum Type
            {
                [EnumMember(Value = "LevelUp")] LEVEL_UP,
                [EnumMember(Value = "TappableCollected")] TAPPABLE,
                [EnumMember(Value = "JournalContentCollected")] JOURNAL_ITEM_UNLOCKED,
                [EnumMember(Value = "CraftingJobCompleted")] CRAFTING_COMPLETED,
                [EnumMember(Value = "SmeltingJobCompleted")] SMELTING_COMPLETED
            }
        }
    }
}
