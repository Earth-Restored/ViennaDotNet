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

    private static readonly Lock _initLock = new Lock();
    private static volatile bool _isInitialized = false;

    public static int AirId { get; private set; }
    public static int WaterId { get; private set; }

    private static void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            lock (_initLock)
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException("Data has not been initialized." + new StackFrame().ToString());
                }
            }
        }
    }

    public static void Initialize(string staticData)
    {
        if (!_isInitialized)
        {
            lock (_initLock)
            {
                if (!_isInitialized)
                {
                    InitializeInternal(staticData);
                    _isInitialized = true;
                }
            }
        }
    }

    private static void InitializeInternal(string staticData)
    {
        DataFile.Load(Path.Combine(staticData, "registry", "blocks_bedrock.json"), _root =>
        {
            var root = (JsonArray)_root;
            foreach (var _element in root)
            {
                var element = _element as JsonObject;
                Debug.Assert(element is not null);

                int id = element["id"]!.GetValue<int>();
                string name = element["name"]!.GetValue<string>()!;
                SortedDictionary<string, object> state = [];
                var stateObject = (JsonObject)element["state"]!;
                foreach (var entry in stateObject)
                {
                    Debug.Assert(entry.Value is JsonValue);
                    var stateElement = (JsonValue)entry.Value;
                    if (stateElement.GetValueKind() == JsonValueKind.String)
                        state[entry.Key] = stateElement.GetValue<string>()!;
                    else
                        state[entry.Key] = stateElement.GetValue<int>();
                }

                var blockNameAndState = new BlockNameAndState(name, state);
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

        _isInitialized = true;

        AirId = BedrockBlocks.GetId("minecraft:air", []);
        SortedDictionary<string, object> hashMap = new()
        {
            { "liquid_depth", 0 }
        };
        WaterId = BedrockBlocks.GetId("minecraft:water", hashMap);
    }

    public static int GetId(string name, SortedDictionary<string, object> state)
    {
        EnsureInitialized();

        var blockNameAndState = new BlockNameAndState(name, state);
        return stateToIdMap.GetOrDefault(blockNameAndState, -1);
    }

    public static string? GetName(int id)
    {
        EnsureInitialized();

        BlockNameAndState? blockNameAndState = idToStateMap.GetOrDefault(id, null);
        return blockNameAndState?.Name;
    }

    public static Dictionary<string, object>? GetState(int id)
    {
        EnsureInitialized();

        BlockNameAndState? blockNameAndState = idToStateMap.GetOrDefault(id, null);
        if (blockNameAndState is null)
            return null;

        Dictionary<string, object> state = [];
        blockNameAndState.State.ForEach((key, value) => state[key] = value);
        return state;
    }

    public static NbtMap? GetStateNbt(int id)
    {
        EnsureInitialized();

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
            var hash = new HashCode();
            hash.Add(Name, StringComparer.Ordinal);
            foreach (var kvp in State)
            {
                hash.Add(kvp.Key, StringComparer.Ordinal);
                hash.Add(kvp.Value);
            }
            
            return hash.ToHashCode();
        }

        public override bool Equals(object? obj)
            => obj is BlockNameAndState other && Name.Equals(other.Name, StringComparison.Ordinal) && State.SequenceEqual(other.State);
    }
}
