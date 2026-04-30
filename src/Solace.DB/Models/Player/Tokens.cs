using System.Text.Json.Serialization;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Common;

namespace ViennaDotNet.DB.Models.Player;

public sealed class Tokens
{
    [JsonInclude, JsonPropertyName("tokens")]
    public Dictionary<string, Token> _tokens;

    public Tokens()
    {
        _tokens = [];
    }

    public Tokens Copy()
    {
        Tokens tokens = new Tokens();
        tokens._tokens.AddRange(_tokens);
        return tokens;
    }

    public sealed record TokenWithId(
        string Id,
        Token Token
    );

    public TokenWithId[] GetTokens()
        => [.. _tokens.Select(item => new TokenWithId(item.Key, item.Value))];

    public void AddToken(string id, Token token)
        => _tokens[id] = token;

    public Token? RemoveToken(string id)
    {
        Token? res = null;
        if (_tokens.TryGetValue(id, out Token? t))
        {
            res = t;
        }

        _tokens.Remove(id);

        return res;
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(LevelUpToken), "LEVEL_UP")]
    [JsonDerivedType(typeof(JournalItemUnlockedToken), "JOURNAL_ITEM_UNLOCKED")]
    public abstract class Token
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
            LEVEL_UP,
            JOURNAL_ITEM_UNLOCKED
        }
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
    }

    public sealed class JournalItemUnlockedToken : Token
    {
        public string ItemId { get; init; }

        public JournalItemUnlockedToken(string itemId)
            : base(TypeE.JOURNAL_ITEM_UNLOCKED)
        {
            ItemId = itemId;
        }
    }
}
