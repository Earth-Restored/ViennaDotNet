using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.PreviewGenerator.NBT;
using ViennaDotNet.PreviewGenerator.Utils;

namespace ViennaDotNet.PreviewGenerator.Registry
{
    public static class BedrockBlocks
    {
        private static readonly Dictionary<BlockNameAndState, int> stateToIdMap = new();
        private static readonly Dictionary<int, BlockNameAndState> idToStateMap = new();

        public static readonly int AIR;
        public static readonly int WATER;

        static BedrockBlocks()
        {
            DataFile.Load("./registry/blocks_bedrock.json", _root =>
            {
                JArray root = (JArray)_root;
                foreach (var _element in root)
                {
                    JObject element = (JObject)_element;
                    int id = element["id"]!.ToObject<int>();
                    string name = element["name"]!.ToObject<string>()!;
                    SortedDictionary<string, object> state = new();
                    JObject stateObject = (JObject)element["state"]!;
                    foreach (var entry in stateObject)
                    {
                        JToken stateElement = entry.Value!;
                        if (stateElement.Type == JTokenType.String)
                            state[entry.Key] = stateElement.ToObject<string>()!;
                        else
                            state[entry.Key] = stateElement.ToObject<int>();
                    }
                    BlockNameAndState blockNameAndState = new BlockNameAndState(name, state);
                    if (stateToIdMap.ContainsKey(blockNameAndState))
                        Log.Warning($"Duplicate Bedrock block name/state {name}");
                    else
                        stateToIdMap.Add(blockNameAndState, id);

                    if (idToStateMap.ContainsKey(id))
                        Log.Warning($"Duplicate Bedrock block ID {id}");
                    else
                        idToStateMap.Add(id, blockNameAndState);
                }
            });

            AIR = BedrockBlocks.getId("minecraft:air", new());
            SortedDictionary<string, object> hashMap = new();
            hashMap.Add("liquid_depth", 0);
            WATER = BedrockBlocks.getId("minecraft:water", hashMap);
        }

        public static int getId(string name, SortedDictionary<string, object> state)
        {
            BlockNameAndState blockNameAndState = new BlockNameAndState(name, state);
            return stateToIdMap.GetOrDefault(blockNameAndState, -1);
        }

        public static string? getName(int id)
        {
            BlockNameAndState? blockNameAndState = idToStateMap.GetOrDefault(id, null);
            return blockNameAndState != null ? blockNameAndState.name : null;
        }

        public static Dictionary<string, object>? getState(int id)
        {
            BlockNameAndState? blockNameAndState = idToStateMap.GetOrDefault(id, null);
            if (blockNameAndState == null)
                return null;

            Dictionary<string, object> state = new();
            blockNameAndState.state.ForEach((key, value) => state[key] = value);
            return state;
        }

        public static NbtMap? getStateNbt(int id)
        {
            BlockNameAndState? blockNameAndState = idToStateMap.GetOrDefault(id, null);
            if (blockNameAndState == null)
                return null;

            NbtMapBuilder builder = NbtMap.builder();
            blockNameAndState.state.ForEach((key, value) =>
            {
                if (value is string s)
                    builder.putString(key, s);
                else if (value is int i)
                    builder.putInt(key, i);
                else
                    throw new InvalidOperationException();
            });
            return builder.build();
        }

        private sealed class BlockNameAndState
        {
            public readonly string name;
            public readonly SortedDictionary<string, object> state;

            public BlockNameAndState(string name, SortedDictionary<string, object> state)
            {
                this.name = name;
                this.state = state;
            }

            public override int GetHashCode()
            {
                unchecked // Overflow is fine, just wrap
                {
                    int hash = 17 * name.GetHashCode();
                    foreach (var kvp in state)
                    {
                        hash = hash * 23 + kvp.Key.GetHashCode();
                        hash = hash * 23 + (kvp.Value?.GetHashCode() ?? 0);
                    }
                    return hash;
                }
            }

            public override bool Equals(object? obj)
            {
                if (obj is BlockNameAndState bnas)
                    return name.Equals(bnas.name) && state.SequenceEqual(bnas.state);
                else
                    return false;
            }
        }
    }
}
