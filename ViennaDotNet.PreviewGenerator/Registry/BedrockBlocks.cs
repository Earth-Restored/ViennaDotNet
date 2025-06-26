using Serilog;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.PreviewGenerator.NBT;
using ViennaDotNet.PreviewGenerator.Utils;

namespace ViennaDotNet.PreviewGenerator.Registry;

public static class BedrockBlocks
{
    private static readonly Dictionary<BlockNameAndState, int> stateToIdMap = [];
    private static readonly Dictionary<int, BlockNameAndState> idToStateMap = [];

    public static readonly int AirId;
    public static readonly int WaterId;

    static BedrockBlocks()
    {
        DataFile.Load("./staticdata/registry/blocks_bedrock.json", _root =>
        {
            JsonArray root = (JsonArray)_root;
            foreach (var _element in root)
            {
                JsonObject? element = _element as JsonObject;
                Debug.Assert(element is not null);

                int id = element["id"]!.GetValue<int>();
                string name = element["name"]!.GetValue<string>()!;
                SortedDictionary<string, object> state = [];
                JsonObject stateObject = (JsonObject)element["state"]!;
                foreach (var entry in stateObject)
                {
                    Debug.Assert(entry.Value is JsonValue);
                    JsonValue stateElement = (JsonValue)entry.Value;
                    if (stateElement.GetValueKind() == JsonValueKind.String)
                        state[entry.Key] = stateElement.GetValue<string>()!;
                    else
                        state[entry.Key] = stateElement.GetValue<int>();
                }

                BlockNameAndState blockNameAndState = new BlockNameAndState(name, state);
                if (stateToIdMap.ContainsKey(blockNameAndState))
                    Log.Warning($"Duplicate Bedrock block name/state {name}", StringComparison.Ordinal);
                else
                    stateToIdMap.Add(blockNameAndState, id);

                if (idToStateMap.ContainsKey(id))
                    Log.Warning($"Duplicate Bedrock block ID {id}", StringComparison.Ordinal);
                else
                    idToStateMap.Add(id, blockNameAndState);
            }
        });

        AirId = BedrockBlocks.GetId("minecraft:air", []);
        SortedDictionary<string, object> hashMap = new()
        {
            { "liquid_depth", 0 }
        };
        WaterId = BedrockBlocks.GetId("minecraft:water", hashMap);
    }

    public static int GetId(string name, SortedDictionary<string, object> state)
    {
        BlockNameAndState blockNameAndState = new BlockNameAndState(name, state);
        return stateToIdMap.GetOrDefault(blockNameAndState, -1);
    }

    public static string? GetName(int id)
    {
        BlockNameAndState? blockNameAndState = idToStateMap.GetOrDefault(id, null);
        return blockNameAndState?.Name;
    }

    public static Dictionary<string, object>? GetState(int id)
    {
        BlockNameAndState? blockNameAndState = idToStateMap.GetOrDefault(id, null);
        if (blockNameAndState is null)
            return null;

        Dictionary<string, object> state = [];
        blockNameAndState.State.ForEach((key, value) => state[key] = value);
        return state;
    }

    public static NbtMap? GetStateNbt(int id)
    {
        BlockNameAndState? blockNameAndState = idToStateMap.GetOrDefault(id, null);
        if (blockNameAndState is null)
            return null;

        NbtMapBuilder builder = NbtMap.builder();
        blockNameAndState.State.ForEach((key, value) =>
        {
            if (value is string s)
                builder.PutString(key, s);
            else if (value is int i)
                builder.PutInt(key, i);
            else
                throw new InvalidOperationException();
        });
        return builder.Build();
    }

    private sealed class BlockNameAndState
    {
        public readonly string Name;
        public readonly SortedDictionary<string, object> State;

        public BlockNameAndState(string name, SortedDictionary<string, object> state)
        {
            Name = name;
            State = state;
        }

        public override int GetHashCode()
        {
            unchecked // Overflow is fine, just wrap
            {
                int hash = 17 * Name.GetHashCode(StringComparison.Ordinal);
                foreach (var kvp in State)
                {
                    hash = hash * 23 + kvp.Key.GetHashCode(StringComparison.Ordinal);
                    hash = hash * 23 + (kvp.Value?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }

        public override bool Equals(object? obj)
            => obj is BlockNameAndState other && Name.Equals(other.Name, StringComparison.Ordinal) && State.SequenceEqual(other.State);
    }
}
