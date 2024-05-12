using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Common;

namespace ViennaDotNet.DB.Models.Player
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Tokens
    {
        [JsonProperty]
        private readonly Dictionary<string, Token> tokens;

        public Tokens()
        {
            tokens = new();
        }

        public Tokens copy()
        {
            Tokens tokens = new Tokens();
            tokens.tokens.AddRange(this.tokens);
            return tokens;
        }

        public record TokenWithId(
            string id,
            Token token
        )
        {
        }

        public TokenWithId[] getTokens()
        {
            return tokens.Select(item => new TokenWithId(item.Key, item.Value)).ToArray();
        }

        public void addToken(string id, Token token)
        {
            tokens[id] = token;
        }

        public Token? removeToken(string id)
        {
            Token? res = null;
            if (tokens.TryGetValue(id, out Token? t))
                res = t;

            tokens.Remove(id);

            return res;
        }

        public abstract class Token
        {
            public readonly Type type;

            public Token(Type type)
            {
                this.type = type;
            }

            public enum Type
            {
                LEVEL_UP,
                JOURNAL_ITEM_UNLOCKED
            }
        }

        public class TokenConverter : JsonConverter<Token>
        {
            public override bool CanRead => true;
            public override bool CanWrite => false;

            public override Token? ReadJson(JsonReader reader, System.Type objectType, Token? existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                var jsonObject = JObject.Load(reader);
                var type = jsonObject[nameof(Token.type)]?.ToObject<Token.Type>();

                switch (type)
                {
                    case Token.Type.LEVEL_UP:
                        return jsonObject.ToObject<LevelUpToken>();
                    case Token.Type.JOURNAL_ITEM_UNLOCKED:
                        return jsonObject.ToObject<JournalItemUnlockedToken>();
                    default:
                        throw new JsonSerializationException($"Unexpected token type: {type}");
                }
            }

            public override void WriteJson(JsonWriter writer, Token? value, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }
        }

        public class LevelUpToken : Token
        {
            public readonly int level;

            public LevelUpToken(int level)
                : base(Type.LEVEL_UP)
            {
                this.level = level;
            }
        }

        public class JournalItemUnlockedToken : Token
        {
            public readonly string itemId;

            public JournalItemUnlockedToken(string itemId)
                : base(Type.JOURNAL_ITEM_UNLOCKED)
            {
                this.itemId = itemId;
            }
        }
    }
}
