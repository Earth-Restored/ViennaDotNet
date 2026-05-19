using System.Text.Json.Serialization;
using Solace.Common.Utils;
using Solace.DB.Models.Common;

namespace Solace.DB.Models.Player;

public sealed class TokensEF : IEntityWithId<Guid>, IVersionedEntity, IMergeable<TokensEF>
{
    public Guid Id { get; set; }

    public int Version { get; set; } = 1;

    public Account Account { get; set; } = null!;

    public Dictionary<string, Token> Tokens { get; set; } = [];

    public sealed record TokenWithId(
        string Id,
        Token Token
    );

    public TokenWithId[] GetTokens()
        => [.. Tokens.Select(item => new TokenWithId(item.Key, item.Value))];

    public void AddToken(string id, Token token)
        => Tokens[id] = token;

    public Token? RemoveToken(string id)
    {
        Tokens.Remove(id, out var token);

        return token;
    }

    public async Task MergeWith(TokensEF other, ValueMerger merger)
    {
        merger.CurrentUserId = Id.ToString();
        merger.CurrentUsername = Account?.Username;

        foreach (var item in other.Tokens)
        {
            Tokens[item.Key] = item.Value;
        }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(LevelUpToken), "LEVEL_UP")]
    [JsonDerivedType(typeof(JournalItemUnlockedToken), "JOURNAL_ITEM_UNLOCKED")]
    public abstract class Token : IEquatable<Token>
    {
        [JsonIgnore]
        public TypeE Type { get; init; }

        protected Token(TypeE type)
        {
            Type = type;
        }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum TypeE
        {
#pragma warning disable CA1707 // Identifiers should not contain underscores
            LEVEL_UP,
            JOURNAL_ITEM_UNLOCKED
#pragma warning restore CA1707 // Identifiers should not contain underscores
        }

        public abstract bool Equals(Token? other);

        public override bool Equals(object? obj)
            => Equals(obj as Token);

        public abstract override int GetHashCode();
    }

    public sealed class LevelUpToken : Token
    {
        public int Level { get; init; }
        public Rewards Rewards { get; init; }

        public LevelUpToken(int level, Rewards rewards)
            : base(TypeE.LEVEL_UP)
        {
            Level = level;
            Rewards = rewards;
        }

        public override bool Equals(Token? other)
            => other is LevelUpToken levelUp && Level == levelUp.Level && Rewards.Equals(levelUp.Rewards);

        public override int GetHashCode()
            => HashCode.Combine(Level, Rewards);
    }

    public sealed class JournalItemUnlockedToken : Token
    {
        public string ItemId { get; init; }

        public JournalItemUnlockedToken(string itemId)
            : base(TypeE.JOURNAL_ITEM_UNLOCKED)
        {
            ItemId = itemId;
        }

        public override bool Equals(Token? other)
            => other is JournalItemUnlockedToken itemUnlocked && ItemId == itemUnlocked.ItemId;

        public override int GetHashCode()
            => HashCode.Combine(ItemId);
    }

    public sealed class Legacy : IEquatable<Legacy>
    {
        [JsonInclude, JsonPropertyName("tokens")]
        public Dictionary<string, Token> Tokens;

        public Legacy()
        {
            Tokens = [];
        }

        public sealed record TokenWithId(
            string Id,
            Token Token
        );

        public bool Equals(Legacy? other)
            => other is not null && Tokens.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)).SequenceEqual(other.Tokens.OrderBy(static item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)));

        public override bool Equals(object? obj)
            => Equals(obj as Legacy);

        public override int GetHashCode()
        {
            var hash = new HashCode();

            foreach (var item in Tokens.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                hash.Add(item.Key);
                hash.Add(item.Value);
            }

            return hash.ToHashCode();
        }

        [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
        [JsonDerivedType(typeof(LevelUpToken), "LEVEL_UP")]
        [JsonDerivedType(typeof(JournalItemUnlockedToken), "JOURNAL_ITEM_UNLOCKED")]
        public abstract class Token : IEquatable<Token>
        {
            [JsonIgnore]
            public TypeE Type { get; init; }

            protected Token(TypeE type)
            {
                Type = type;
            }

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum TypeE
            {
#pragma warning disable CA1707 // Identifiers should not contain underscores
                LEVEL_UP,
                JOURNAL_ITEM_UNLOCKED
#pragma warning restore CA1707 // Identifiers should not contain underscores
            }

            public abstract bool Equals(Token? other);

            public override bool Equals(object? obj)
                => Equals(obj as Token);

            public abstract override int GetHashCode();
        }

        public sealed class LevelUpToken : Token
        {
            public int Level { get; init; }
            public Rewards Rewards { get; init; }

            public LevelUpToken(int level, Rewards rewards)
                : base(TypeE.LEVEL_UP)
            {
                Level = level;
                Rewards = rewards;
            }

            public override bool Equals(Token? other)
                => other is LevelUpToken levelUp && Level == levelUp.Level && Rewards.Equals(levelUp.Rewards);

            public override int GetHashCode()
                => HashCode.Combine(Level, Rewards);
        }

        public sealed class JournalItemUnlockedToken : Token
        {
            public string ItemId { get; init; }

            public JournalItemUnlockedToken(string itemId)
                : base(TypeE.JOURNAL_ITEM_UNLOCKED)
            {
                ItemId = itemId;
            }

            public override bool Equals(Token? other)
                => other is JournalItemUnlockedToken itemUnlocked && ItemId == itemUnlocked.ItemId;

            public override int GetHashCode()
                => HashCode.Combine(ItemId);
        }
    }
}
