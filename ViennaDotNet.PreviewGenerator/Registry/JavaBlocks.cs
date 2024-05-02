using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.PreviewGenerator.NBT;
using ViennaDotNet.PreviewGenerator.Utils;

namespace ViennaDotNet.PreviewGenerator.Registry
{
    public static class JavaBlocks
    {
        private static readonly Dictionary<int, string> map = new();
        private static readonly Dictionary<string, LinkedList<string>> nonVanillaStatesList = new();

        private static readonly Dictionary<int, BedrockMapping> bedrockMap = new();
        private static readonly Dictionary<string, BedrockMapping> bedrockMapByName = new();
        private static readonly Dictionary<string, BedrockMapping> bedrockNonVanillaMap = new();

        static JavaBlocks()
        {
            DataFile.Load("./registry/blocks_java.json", jToken =>
            {
                JArray jArray = (JArray)jToken;

                foreach (var _element in jArray)
                {
                    JObject element = (JObject)_element;
                    int id = element["id"]!.ToObject<int>();
                    string name = element["name"]!.ToObject<string>()!;
                    if (map.ContainsKey(id))
                        Log.Warning($"Duplicate Java block ID {id}");
                    else
                        map.Add(id, name);

                    try
                    {
                        BedrockMapping? bedrockMapping = readBedrockMapping((JObject)element["bedrock"]!, jArray);
                        if (bedrockMapping == null)
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

            DataFile.Load("./registry/blocks_java_nonvanilla.json", jToken =>
            {
                JArray jArray = (JArray)jToken;

                foreach (var _element in jArray)
                {
                    JObject element = (JObject)_element;

                    string baseName = element["name"]!.ToObject<string>()!;

                    LinkedList<string> stateNames = new();
                    JArray statesArray = (JArray)element["states"]!;
                    foreach (var _stateElement in statesArray)
                    {
                        JObject stateElement = (JObject)_stateElement;

                        string stateName = stateElement["name"]!.ToObject<string>()!;
                        stateNames.AddLast(stateName);

                        string name = baseName + stateName;

                        try
                        {
                            BedrockMapping? bedrockMapping = readBedrockMapping((JObject)stateElement["bedrock"]!, null);
                            if (bedrockMapping == null)
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

        private static BedrockMapping? readBedrockMapping(JObject bedrockMappingObject, JArray? javaBlocksArray)
        {
            if (bedrockMappingObject.TryGetValue("ignore", out JToken? ignoreToken) && ignoreToken.ToObject<bool>())
                return null;

            string name = bedrockMappingObject["name"]!.ToObject<string>()!;

            SortedDictionary<string, object> state = new();
            if (bedrockMappingObject.TryGetValue("state", out JToken? stateToken))
            {
                JObject stateObject = (JObject)stateToken;
                foreach (var entry in stateObject)
                {
                    JToken stateElement = entry.Value!;
                    if (stateElement.Type == JTokenType.String)
                        state[entry.Key] = stateElement.ToObject<string>()!;
                    else if (stateElement.Type == JTokenType.Boolean)
                        state[entry.Key] = stateElement.ToObject<bool>() ? 1 : 0;
                    else
                        state[entry.Key] = stateElement.ToObject<int>();
                }
            }

            int id = BedrockBlocks.getId(name, state);
            if (id == -1)
                throw new BedrockMappingFailException("Cannot find Bedrock block with provided name and state");

            bool waterlogged = bedrockMappingObject.TryGetValue("waterlogged", out JToken? waterloggedToken) ? waterloggedToken.ToObject<bool>() : false;

            BedrockMapping.BlockEntity? blockEntity = null;
            if (bedrockMappingObject.TryGetValue("block_entity", out JToken? blockEntityToken))
            {
                JObject blockEntityObject = (JObject)blockEntityToken;
                string type = blockEntityObject["type"]!.ToObject<string>()!;
                switch (type)
                {
                    case "bed":
                        {
                            string color = blockEntityObject["color"]!.ToObject<string>()!;
                            blockEntity = new BedrockMapping.BedBlockEntity(type, color);
                        }
                        break;
                    case "flower_pot":
                        {
                            NbtMap? contents = null;
                            if (blockEntityObject.TryGetValue("contents", out JToken? contentsToken) && contentsToken.Type != JTokenType.Null)
                            {
                                string contentsName = contentsToken.ToObject<string>()!;
                                if (javaBlocksArray != null)
                                {
                                    contents = javaBlocksArray
                                        .Where(element => ((JObject)element)["name"]!.ToObject<string>() == contentsName)
                                        .Select(element => ((JObject)((JObject)element)["bedrock"]!))
                                            .Where(element => !element.ContainsKey("ignore") || !element["ignore"]!.ToObject<bool>())
                                            .FirstOrDefault()!.Map(element =>
                                    {
                                        NbtMapBuilder builder = NbtMap.builder();
                                        builder.putString("name", element["name"]!.ToObject<string>()!);
                                        if (element.TryGetValue("state", out JToken? stateToken))
                                        {
                                            NbtMapBuilder stateBuilder = NbtMap.builder();
                                            ((JObject)stateToken).ForEach((key, stateElement) =>
                                            {
                                                if (stateElement!.Type == JTokenType.String)
                                                    stateBuilder.putString(key, stateElement.ToObject<string>()!);
                                                else if (stateElement.Type == JTokenType.Boolean)
                                                    stateBuilder.putInt(key, stateElement.ToObject<bool>() ? 1 : 0);
                                                else
                                                    stateBuilder.putInt(key, stateElement.ToObject<int>());
                                            });
                                            builder.putCompound("states", stateBuilder.build());
                                        }
                                        return builder.build();
                                    });
                                }
                                if (contents == null)
                                    throw new BedrockMappingFailException("Could not find contents for flower pot");
                            }
                            blockEntity = new BedrockMapping.FlowerPotBlockEntity(type, contents);
                        }
                        break;
                    case "moving_block":
                        {
                            blockEntity = new BedrockMapping.BlockEntity(type);
                        }
                        break;
                    case "piston":
                        {
                            bool sticky = blockEntityObject["sticky"]!.ToObject<bool>();
                            bool extended = blockEntityObject["extended"]!.ToObject<bool>();
                            blockEntity = new BedrockMapping.PistonBlockEntity(type, sticky, extended);
                        }
                        break;
                }
            }

            BedrockMapping.ExtraData? extraData = null;
            if (bedrockMappingObject.TryGetValue("extra_data", out JToken? extra_dataToken))
            {
                JObject extraDataObject = (JObject)extra_dataToken;
                string type = extraDataObject["type"]!.ToObject<string>()!;
                switch (type)
                {
                    case "note_block":
                        {
                            int pitch = extraDataObject["pitch"]!.ToObject<int>();
                            extraData = new BedrockMapping.NoteBlockExtraData(pitch);
                        }
                        break;
                }
            }

            return new BedrockMapping(id, waterlogged, blockEntity, extraData);
        }

        private class BedrockMappingFailException : Exception
        {
            public BedrockMappingFailException(string? message)
                : base(message)
            {
            }
        }

        public static int getMaxVanillaBlockId()
        {
            if (map.Count == 0) return -1;
            else return map.Keys.Max();
        }

        public static string[]? getStatesForNonVanillaBlock(string name)
        {
            LinkedList<string>? states = nonVanillaStatesList.GetOrDefault(name, null);
            return states != null ? states.ToArray() : null;
        }

        [Obsolete]
        public static string? getName(int id)
        {
            return getName(id, null);
        }

        [Obsolete]
        public static BedrockMapping? getBedrockMapping(int javaId)
        {
            return getBedrockMapping(javaId, null);
        }

        public static string? getName(int id, /*FabricRegistryManager?*/object? fabricRegistryManager)
        {
            string? name = map.GetOrDefault(id, null);
            if (name == null && fabricRegistryManager != null)
                name = null;//fabricRegistryManager.getBlockName(id);

            return name;
        }

        public static BedrockMapping? getBedrockMapping(int javaId, /*FabricRegistryManager?*/object? fabricRegistryManager)
        {
            BedrockMapping? bedrockMapping = bedrockMap.GetOrDefault(javaId, null);
            if (bedrockMapping == null && fabricRegistryManager != null)
            {
                string? fabricName = null;//fabricRegistryManager.getBlockName(javaId);
                if (fabricName != null)
                    bedrockMapping = bedrockNonVanillaMap.GetOrDefault(fabricName, null);
            }
            return bedrockMapping;
        }

        public static BedrockMapping? getBedrockMapping(string javaName)
        {
            BedrockMapping? bedrockMapping = bedrockMapByName.GetOrDefault(javaName, null);
            if (bedrockMapping == null)
                bedrockMapping = bedrockNonVanillaMap.GetOrDefault(javaName, null);

            return bedrockMapping;
        }

        public sealed class BedrockMapping
        {
            public readonly int id;
            public readonly bool waterlogged;
            public readonly BlockEntity? blockEntity;
            public readonly ExtraData? extraData;

            public BedrockMapping(int id, bool waterlogged, BlockEntity? blockEntity, ExtraData? extraData)
            {
                this.id = id;
                this.waterlogged = waterlogged;
                this.blockEntity = blockEntity;
                this.extraData = extraData;
            }

            public class BlockEntity
            {
                public readonly string type;

                public BlockEntity(string type)
                {
                    this.type = type;
                }
            }

            public class BedBlockEntity : BlockEntity
            {
                public readonly string color;

                public BedBlockEntity(string type, string color)
                    : base(type)
                {
                    this.color = color;
                }
            }

            public class FlowerPotBlockEntity : BlockEntity
            {
                public readonly NbtMap? contents;

                public FlowerPotBlockEntity(string type, NbtMap? contents)
                    : base(type)
                {
                    this.contents = contents;
                }
            }

            public class PistonBlockEntity : BlockEntity
            {
                public readonly bool sticky;
                public readonly bool extended;

                public PistonBlockEntity(string type, bool sticky, bool extended)
                    : base(type)
                {
                    this.sticky = sticky;
                    this.extended = extended;
                }
            }

            public abstract class ExtraData
            {
                protected ExtraData()
                {
                    // empty
                }
            }

            public class NoteBlockExtraData : ExtraData
            {
                public readonly int pitch;

                public NoteBlockExtraData(int pitch)
                    : base()
                {
                    this.pitch = pitch;
                }
            }
        }
    }
}
