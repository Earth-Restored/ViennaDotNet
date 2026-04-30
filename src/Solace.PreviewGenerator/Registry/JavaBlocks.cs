using Serilog;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.PreviewGenerator.NBT;
using ViennaDotNet.PreviewGenerator.Utils;

namespace ViennaDotNet.PreviewGenerator.Registry;

public static class JavaBlocks
{
    private static readonly Dictionary<int, string> map = [];
    private static readonly Dictionary<string, LinkedList<string>> nonVanillaStatesList = [];

    private static readonly Dictionary<int, BedrockMapping> bedrockMap = [];
    private static readonly Dictionary<string, BedrockMapping> bedrockMapByName = [];
    private static readonly Dictionary<string, BedrockMapping> bedrockNonVanillaMap = [];

    private static readonly Lock _initLock = new Lock();
    private static volatile bool _isInitialized = false;

     private static void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            lock (_initLock)
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException("Data has not been initialized."+ new StackFrame().ToString());
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
        DataFile.Load(Path.Combine(staticData, "registry", "blocks_java.json"), jToken =>
        {
            var jArray = (JsonArray)jToken;

            foreach (var _element in jArray)
            {
                var element = _element as JsonObject;
                Debug.Assert(element is not null);

                int id = element["id"]!.GetValue<int>();
                string name = element["name"]!.GetValue<string>()!;
                if (map.ContainsKey(id))
                    Log.Warning($"Duplicate Java block ID {id}");
                else
                    map.Add(id, name);

                try
                {
                    BedrockMapping? bedrockMapping = ReadBedrockMapping((JsonObject)element["bedrock"]!, jArray);
                    if (bedrockMapping is null)
                    {
                        Log.Debug($"Ignoring Java block {name}");
                        continue;
                    }

                    bedrockMap[id] = bedrockMapping;
                    bedrockMapByName[name] = bedrockMapping;
                }
                catch (BedrockMappingFailException ex)
                {
                    Log.Warning($"Cannot find Bedrock block for Java block {name}: {ex.Message}");
                }
            }
        });

        DataFile.Load(Path.Combine(staticData, "registry", "blocks_java_nonvanilla.json"), jToken =>
        {
            var jArray = (JsonArray)jToken;

            foreach (var _element in jArray)
            {
                var element = _element as JsonObject;
                Debug.Assert(element is not null);

                string baseName = element["name"]!.GetValue<string>()!;

                LinkedList<string> stateNames = new();
                var statesArray = (JsonArray)element["states"]!;
                foreach (var _stateElement in statesArray)
                {
                    var stateElement = _stateElement as JsonObject;
                    Debug.Assert(stateElement is not null);

                    string stateName = stateElement["name"]!.GetValue<string>()!;
                    stateNames.AddLast(stateName);

                    string name = baseName + stateName;

                    try
                    {
                        BedrockMapping? bedrockMapping = ReadBedrockMapping((JsonObject)stateElement["bedrock"]!, null);
                        if (bedrockMapping is null)
                        {
                            Log.Debug($"Ignoring Java block {name}");
                            continue;
                        }

                        bedrockNonVanillaMap[name] = bedrockMapping;
                    }
                    catch (BedrockMappingFailException ex)
                    {
                        Log.Warning($"Cannot find Bedrock block for Java block {name}: {ex.Message}");
                    }
                }

                if (nonVanillaStatesList.ContainsKey(baseName))
                    Log.Warning($"Duplicate Java non-vanilla block name {baseName}");
                else
                    nonVanillaStatesList.Add(baseName, stateNames);
            }
        });
    }

    private static BedrockMapping? ReadBedrockMapping(JsonObject bedrockMappingObject, JsonArray? javaBlocksArray)
    {
        if (bedrockMappingObject.TryGetPropertyValue("ignore", out var ignoreToken) && ignoreToken!.GetValue<bool>())
            return null;

        string name = bedrockMappingObject["name"]!.GetValue<string>()!;

        SortedDictionary<string, object> state = [];
        if (bedrockMappingObject.TryGetPropertyValue("state", out var stateToken))
        {
            var stateObject = stateToken as JsonObject;
            Debug.Assert(stateObject is not null);

            foreach (var entry in stateObject)
            {
                var stateElement = entry.Value as JsonValue;
                Debug.Assert(stateElement is not null);
                var stateElementType = stateElement.GetValueKind();
                if (stateElementType == JsonValueKind.String)
                    state[entry.Key] = stateElement.GetValue<string>()!;
                else if (stateElementType == JsonValueKind.True)
                    state[entry.Key] = 1;
                else if (stateElementType == JsonValueKind.False)
                    state[entry.Key] = 0;
                else
                    state[entry.Key] = stateElement.GetValue<int>();
            }
        }

        int id = BedrockBlocks.GetId(name, state);
        if (id == -1)
        {
            throw new BedrockMappingFailException("Cannot find Bedrock block with provided name and state");
        }

        bool waterlogged = bedrockMappingObject.TryGetPropertyValue("waterlogged", out var waterloggedToken) && waterloggedToken!.GetValue<bool>();

        BedrockMapping.BlockEntityR? blockEntity = null;
        if (bedrockMappingObject.TryGetPropertyValue("block_entity", out var blockEntityToken))
        {
            var blockEntityObject = blockEntityToken as JsonObject;
            Debug.Assert(blockEntityObject is not null);

            string type = blockEntityObject["type"]!.GetValue<string>()!;
            switch (type)
            {
                case "bed":
                    {
                        string color = blockEntityObject["color"]!.GetValue<string>()!;
                        blockEntity = new BedrockMapping.BedBlockEntity(type, color);
                    }

                    break;
                case "flower_pot":
                    {
                        NbtMap? contents = null;
                        if (blockEntityObject.TryGetPropertyValue("contents", out var contentsToken) && contentsToken!.GetValueKind() is not JsonValueKind.Null)
                        {
                            string contentsName = contentsToken.GetValue<string>()!;
                            if (javaBlocksArray is not null)
                            {
                                contents = javaBlocksArray
                                    .Where(element => ((JsonObject)element!)["name"]!.GetValue<string>() == contentsName)
                                    .Select(element => (JsonObject)((JsonObject)element!)["bedrock"]!)
                                    .Where(element => !element.ContainsKey("ignore") || !element["ignore"]!.GetValue<bool>())
                                    .FirstOrDefault()!.Map(element =>
                                    {
                                        NbtMapBuilder builder = NbtMap.builder();
                                        builder.PutString("name", element["name"]!.GetValue<string>()!);
                                        if (element.TryGetPropertyValue("state", out var stateToken))
                                        {
                                            Debug.Assert(stateToken is not null);

                                            NbtMapBuilder stateBuilder = NbtMap.builder();
                                            ((JsonObject)stateToken).ForEach((key, stateElement) =>
                                            {
                                                Debug.Assert(stateElement is not null);

                                                var stateElementType = stateElement.GetValueKind();
                                                if (stateElementType == JsonValueKind.String)
                                                    stateBuilder.PutString(key, stateElement.GetValue<string>()!);
                                                else if (stateElementType == JsonValueKind.True)
                                                    stateBuilder.PutInt(key, 1);
                                                else if (stateElementType == JsonValueKind.False)
                                                    stateBuilder.PutInt(key, 0);
                                                else
                                                    stateBuilder.PutInt(key, stateElement.GetValue<int>());
                                            });
                                            builder.PutCompound("states", stateBuilder.Build());
                                        }

                                        return builder.Build();
                                    });
                            }

                            if (contents is null)
                                throw new BedrockMappingFailException("Could not find contents for flower pot");
                        }

                        blockEntity = new BedrockMapping.FlowerPotBlockEntity(type, contents);
                    }

                    break;
                case "moving_block":
                    {
                        blockEntity = new BedrockMapping.BlockEntityR(type);
                    }

                    break;
                case "piston":
                    {
                        bool sticky = blockEntityObject["sticky"]!.GetValue<bool>();
                        bool extended = blockEntityObject["extended"]!.GetValue<bool>();
                        blockEntity = new BedrockMapping.PistonBlockEntity(type, sticky, extended);
                    }

                    break;
            }
        }

        BedrockMapping.ExtraDataR? extraData = null;
        if (bedrockMappingObject.TryGetPropertyValue("extra_data", out var extra_dataToken))
        {
            var extraDataObject = extra_dataToken as JsonObject;
            Debug.Assert(extraDataObject is not null);

            string type = extraDataObject["type"]!.GetValue<string>();
            switch (type)
            {
                case "note_block":
                    {
                        int pitch = extraDataObject["pitch"]!.GetValue<int>();
                        extraData = new BedrockMapping.NoteBlockExtraData(pitch);
                    }

                    break;
            }
        }

        return new BedrockMapping(id, waterlogged, blockEntity, extraData);
    }

    private sealed class BedrockMappingFailException : Exception
    {
        public BedrockMappingFailException(string? message)
            : base(message)
        {
        }
    }

    public static int GetMaxVanillaBlockId()
    {
        EnsureInitialized();

        if (map.Count == 0) return -1;
        else return map.Keys.Max();
    }

    public static string[]? GetStatesForNonVanillaBlock(string name)
    {
        EnsureInitialized();

        LinkedList<string>? states = nonVanillaStatesList.GetOrDefault(name, null);
        return states?.ToArray();
    }

    [Obsolete]
    public static string? GetName(int id)
        => GetName(id, null);

    [Obsolete]
    public static BedrockMapping? GetBedrockMapping(int javaId)
        => GetBedrockMapping(javaId, null);

    // TODO?: FabricRegistryManager
    public static string? GetName(int id, /*FabricRegistryManager?*/object? fabricRegistryManager)
    {
        EnsureInitialized();

        string? name = map.GetOrDefault(id, null);
        if (name is null && fabricRegistryManager is not null)
            name = null;//fabricRegistryManager.getBlockName(id);

        return name;
    }

    // TODO?: FabricRegistryManager
    public static BedrockMapping? GetBedrockMapping(int javaId, /*FabricRegistryManager?*/object? fabricRegistryManager)
    {
        EnsureInitialized();

        BedrockMapping? bedrockMapping = bedrockMap.GetOrDefault(javaId, null);
        if (bedrockMapping is null && fabricRegistryManager is not null)
        {
            string? fabricName = null;//fabricRegistryManager.getBlockName(javaId);
            if (fabricName is not null)
                bedrockMapping = bedrockNonVanillaMap.GetOrDefault(fabricName, null);
        }

        return bedrockMapping;
    }

    public static BedrockMapping? GetBedrockMapping(string javaName)
    {
        EnsureInitialized();

        BedrockMapping? bedrockMapping = bedrockMapByName.GetOrDefault(javaName, null) ?? bedrockNonVanillaMap.GetOrDefault(javaName, null);
        return bedrockMapping;
    }

    public sealed class BedrockMapping
    {
        public readonly int Id;
        public readonly bool Waterlogged;
        public readonly BlockEntityR? BlockEntity;
        public readonly ExtraDataR? ExtraData;

        public BedrockMapping(int id, bool waterlogged, BlockEntityR? blockEntity, ExtraDataR? extraData)
        {
            Id = id;
            Waterlogged = waterlogged;
            BlockEntity = blockEntity;
            ExtraData = extraData;
        }

        public class BlockEntityR
        {
            public readonly string Type;

            public BlockEntityR(string type)
            {
                Type = type;
            }
        }

        public class BedBlockEntity : BlockEntityR
        {
            public readonly string Color;

            public BedBlockEntity(string type, string color)
                : base(type)
            {
                Color = color;
            }
        }

        public class FlowerPotBlockEntity : BlockEntityR
        {
            public readonly NbtMap? Contents;

            public FlowerPotBlockEntity(string type, NbtMap? contents)
                : base(type)
            {
                Contents = contents;
            }
        }

        public class PistonBlockEntity : BlockEntityR
        {
            public readonly bool Sticky;
            public readonly bool Extended;

            public PistonBlockEntity(string type, bool sticky, bool extended)
                : base(type)
            {
                Sticky = sticky;
                Extended = extended;
            }
        }

        public abstract class ExtraDataR
        {
            protected ExtraDataR()
            {
                // empty
            }
        }

        public class NoteBlockExtraData : ExtraDataR
        {
            public readonly int Pitch;

            public NoteBlockExtraData(int pitch)
                : base()
            {
                Pitch = pitch;
            }
        }
    }
}
