using Newtonsoft.Json;
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

        public record Token(
            Token.Type type,
            Rewards rewards,
            Token.Lifetime lifetime,
            Dictionary<string, string> properties
        )
        {
            public enum Type
            {
                LEVEL_UP,
                JOURNAL_ITEM_UNLOCKED
            }

            public enum Lifetime
            {
                PERSISTENT,
                TRANSIENT
            }
        }
    }
}
