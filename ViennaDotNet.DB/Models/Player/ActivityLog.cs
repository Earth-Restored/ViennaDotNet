using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Common;

namespace ViennaDotNet.DB.Models.Player
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ActivityLog
    {
        [JsonProperty]
        private readonly LinkedList<Entry> entries;

        public ActivityLog()
        {
            entries = new();
        }

        public ActivityLog copy()
        {
            ActivityLog activityLog = new ActivityLog();
            activityLog.entries.AddRange(entries);
            return activityLog;
        }

        public Entry[] getEntries()
        {
            return entries.ToArray();
        }

        public void addEntry(Entry entry)
        {
            entries.AddLast(entry);
        }

        [JsonObject(MemberSerialization.OptIn)]
        public abstract class Entry
        {
            [JsonProperty]
            public readonly long timestamp;
            [JsonProperty]
            public readonly Type type;

            protected Entry(long timestamp, Type type)
            {
                this.timestamp = timestamp;
                this.type = type;
            }

            public enum Type
            {
                LEVEL_UP,
                TAPPABLE,
                JOURNAL_ITEM_UNLOCKED,
                CRAFTING_COMPLETED,
                SMELTING_COMPLETED
            }

            public class EntryConverter : JsonConverter<Entry>
            {
                public override bool CanRead => true;
                public override bool CanWrite => false;

                public override Entry? ReadJson(JsonReader reader, System.Type objectType, Entry? existingValue, bool hasExistingValue, JsonSerializer serializer)
                {
                    JObject jsonObject = JObject.Load(reader);
                    var type = jsonObject[nameof(Entry.type)]?.ToObject<Type>();

                    switch (type)
                    {
                        case Type.LEVEL_UP:
                            return jsonObject.ToObject<LevelUpEntry>();
                        case Type.TAPPABLE:
                            return jsonObject.ToObject<TappableEntry>();
                        case Type.JOURNAL_ITEM_UNLOCKED:
                            return jsonObject.ToObject<JournalItemUnlockedEntry>();
                        case Type.CRAFTING_COMPLETED:
                            return jsonObject.ToObject<CraftingCompletedEntry>();
                        case Type.SMELTING_COMPLETED:
                            return jsonObject.ToObject<SmeltingCompletedEntry>();
                        default:
                            throw new JsonSerializationException("Invalid token type.");
                    }
                }

                public override void WriteJson(JsonWriter writer, Entry? value, JsonSerializer serializer)
                {
                    throw new NotImplementedException();
                }
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public sealed class LevelUpEntry : Entry
        {
            [JsonProperty]
            public readonly int level;

            public LevelUpEntry(long timestamp, int level)
                : base(timestamp, Type.LEVEL_UP)
            {
                this.level = level;
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public sealed class TappableEntry : Entry
        {
            [JsonProperty]
            public readonly Rewards rewards;

            public TappableEntry(long timestamp, Rewards rewards)
                : base(timestamp, Type.TAPPABLE)
            {
                this.rewards = rewards;
            }
        }
        [JsonObject(MemberSerialization.OptIn)]

        public sealed class JournalItemUnlockedEntry : Entry
        {
            [JsonProperty]
            public readonly string itemId;

            public JournalItemUnlockedEntry(long timestamp, string itemId)
                : base(timestamp, Type.JOURNAL_ITEM_UNLOCKED)
            {
                this.itemId = itemId;
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public sealed class CraftingCompletedEntry : Entry
        {
            [JsonProperty]
            public readonly Rewards rewards;

            public CraftingCompletedEntry(long timestamp, Rewards rewards)
                : base(timestamp, Type.CRAFTING_COMPLETED)
            {
                this.rewards = rewards;
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public sealed class SmeltingCompletedEntry : Entry
        {
            [JsonProperty]
            public readonly Rewards rewards;

            public SmeltingCompletedEntry(long timestamp, Rewards rewards)
                : base(timestamp, Type.SMELTING_COMPLETED)
            {
                this.rewards = rewards;
            }
        }
    }
}
