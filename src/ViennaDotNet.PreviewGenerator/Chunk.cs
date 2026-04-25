using System.Globalization;
using Serilog;
using SharpNBT;
using ViennaDotNet.PreviewGenerator.BlockEntity;
using ViennaDotNet.PreviewGenerator.NBT;
using ViennaDotNet.PreviewGenerator.Registry;
using ViennaDotNet.PreviewGenerator.Utils;

namespace ViennaDotNet.PreviewGenerator;

internal sealed class Chunk
{
    public static Chunk? Read(CompoundTag chunkTag)
    {
        try
        {
            return new Chunk(chunkTag);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not read chunk: {ex}");
            return null;
        }
    }

    public readonly int ChunkX;
    public readonly int ChunkZ;

    public readonly int[] Blocks = new int[16 * 256 * 16];
    public readonly NbtMap?[] BlockEntities = new NbtMap[16 * 256 * 16];

    private Chunk(CompoundTag chunkTag)
    {
        ChunkX = chunkTag.Get<IntTag>("xPos");
        ChunkZ = chunkTag.Get<IntTag>("zPos");

        JavaBlocks.BedrockMapping.BlockEntityR?[] blockEntityMappings = new JavaBlocks.BedrockMapping.BlockEntityR[16 * 256 * 16];
        JavaBlocks.BedrockMapping.ExtraDataR?[] extraDatas = new JavaBlocks.BedrockMapping.ExtraDataR[16 * 256 * 16];

        Array.Fill(Blocks, BedrockBlocks.AirId);
        Array.Fill(BlockEntities, null);
        Array.Fill(blockEntityMappings, null);
        Array.Fill(extraDatas, null);

        HashSet<string> alreadyNotifiedMissingBlocks = [];
        for (int subchunkY = 0; subchunkY < 16; subchunkY++)
        {
            int sectionIndex = subchunkY + 4 + 1; // Java world height starts at -64, plus one section for bottommost lighting
            CompoundTag sectionTag = (CompoundTag)chunkTag.Get<ListTag>("sections")[sectionIndex];

            CompoundTag blockStatesTag = sectionTag.Get<CompoundTag>("block_states");

            ListTag paletteTag = blockStatesTag.Get<ListTag>("palette");
            List<string> javaPalette = new(paletteTag.Count);
            foreach (Tag paletteEntryTag in paletteTag)
                javaPalette.Add(ReadPaletteEntry((CompoundTag)paletteEntryTag));

            int[] javaBlocks;
            if (javaPalette.Count == 0)
                throw new IOException("Chunk section has empty palette");

            if (!blockStatesTag.ContainsKey("data"))
            {
                if (javaPalette.Count > 1)
                    throw new IOException("Chunk section has palette with more than one entry and no data");

                javaBlocks = new int[4096];
                Array.Fill(javaBlocks, 0);
            }
            else
                javaBlocks = ReadBitArray(blockStatesTag.Get<LongArrayTag>("data"), javaPalette.Count);

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        string javaName = javaPalette[javaBlocks[(y * 16 + z) * 16 + x]];

                        JavaBlocks.BedrockMapping? bedrockMapping = JavaBlocks.GetBedrockMapping(javaName);
                        if (bedrockMapping is null)
                        {
                            if (alreadyNotifiedMissingBlocks.Add(javaName))
                                Log.Warning($"Chunk contained block with no mapping {javaName}");
                        }

                        // TODO: how to handle waterlogged blocks???
                        int bedrockId = bedrockMapping is not null ? bedrockMapping.Id : BedrockBlocks.AirId;
                        Blocks[(x * 256 + y + subchunkY * 16) * 16 + z] = bedrockId;

                        JavaBlocks.BedrockMapping.BlockEntityR? blockEntityMapping = bedrockMapping is not null && bedrockMapping.BlockEntity is not null ? bedrockMapping.BlockEntity : null;
                        NbtMap? bedrockBlockEntityData = blockEntityMapping is not null ? BlockEntityTranslator.TranslateBlockEntity(blockEntityMapping, null) : null;
                        if (bedrockBlockEntityData is not null)
                            bedrockBlockEntityData = bedrockBlockEntityData.toBuilder().PutInt("x", x + ChunkX * 16).PutInt("y", y + subchunkY * 16).PutInt("z", z + ChunkZ * 16).PutBoolean("isMovable", false).Build();

                        BlockEntities[(x * 256 + y + subchunkY * 16) * 16 + z] = bedrockBlockEntityData;
                        blockEntityMappings[(x * 256 + y + subchunkY * 16) * 16 + z] = blockEntityMapping;

                        extraDatas[(x * 256 + y + subchunkY * 16) * 16 + z] = bedrockMapping?.ExtraData;
                    }
                }
            }
        }

        foreach (Tag blockEntityTag in chunkTag.Get<ListTag>("block_entities"))
        {
            CompoundTag blockEntityCompoundTag = (CompoundTag)blockEntityTag;
            int x = GetChunkBlockOffset(blockEntityCompoundTag.Get<IntTag>("x").Value);
            int y = blockEntityCompoundTag.Get<IntTag>("y").Value;
            int z = GetChunkBlockOffset(blockEntityCompoundTag.Get<IntTag>("z").Value);
            string type = blockEntityCompoundTag.Get<StringTag>("id").Value;
            BlockEntityInfo blockEntityInfo = new BlockEntityInfo(x, y, z, BlockEntityType.FURNACE, blockEntityCompoundTag);    // TODO: use proper type (currently this doesn't matter for any of our translator implementations)

            JavaBlocks.BedrockMapping.BlockEntityR? blockEntityMapping = blockEntityMappings[(x * 256 + y) * 16 + z];
            if (blockEntityMapping is null)
                Log.Debug($"Ignoring block entity of type {type}");

            NbtMap? bedrockBlockEntityData = blockEntityMapping is not null ? BlockEntityTranslator.TranslateBlockEntity(blockEntityMapping, blockEntityInfo) : null;
            if (bedrockBlockEntityData is not null)
                bedrockBlockEntityData = bedrockBlockEntityData.toBuilder().PutInt("x", x + ChunkX * 16).PutInt("y", y).PutInt("z", z + ChunkZ * 16).PutBoolean("isMovable", false).Build();

            BlockEntities[(x * 256 + y) * 16 + z] = bedrockBlockEntityData;
        }
    }

    // TODO: this relies on the state tags in the block names in the Java blocks registry matching the actual server names/values and to be sorted in alphabetical order, should verify/ensure that this is the case
    private static string ReadPaletteEntry(CompoundTag paletteEntryTag)
    {
        string name = paletteEntryTag.Get<StringTag>("Name").Value;

        List<string> properties = [];
        if (paletteEntryTag.ContainsKey("Properties"))
        {
            foreach (Tag propertyTag in paletteEntryTag.Get<CompoundTag>("Properties"))
            {
                properties.Add(propertyTag.Name + "=" + TagValueToString(propertyTag));
            }
        }

        properties.Sort(string.Compare);

        if (properties.Count > 0)
        {
            name = name + "[" + string.Join(",", properties.ToArray()) + "]";
        }

        return name;
    }

    private static int[] ReadBitArray(LongArrayTag longArrayTag, int maxValue)
    {
        int[] @out = new int[4096];
        int outIndex = 0;

        long[] @in = longArrayTag;
        int inIndex = 0;
        int inSubIndex = 0;

        int bits = 64;
        for (int bits1 = 4; bits1 <= 64; bits1++)
        {
            if (maxValue <= (1 << bits1))
            {
                bits = bits1;
                break;
            }
        }

        int valuesPerLong = 64 / bits;

        long currentIn = @in[inIndex++];
        inSubIndex = 0;
        while (outIndex < @out.Length)
        {
            if (inSubIndex >= valuesPerLong)
            {
                currentIn = @in[inIndex++];
                inSubIndex = 0;
            }

            long value = (currentIn >> (inSubIndex++ * bits)) & ((1 << bits) - 1);
            @out[outIndex++] = (int)value;
        }

        return @out;
    }

    private static int GetChunkBlockOffset(int pos)
        => pos >= 0 ? pos % 16 : 15 - ((-pos - 1) % 16);

    private static string TagValueToString(Tag tag)
        => tag switch
        {
            ByteTag @byte => @byte.IsBool ? @byte.Bool.ToString(CultureInfo.InvariantCulture) : @byte.Value.ToString(CultureInfo.InvariantCulture),
            DoubleTag @double => @double.Value.ToString(CultureInfo.InvariantCulture),
            FloatTag @float => @float.Value.ToString(CultureInfo.InvariantCulture),
            IntTag @int => @int.Value.ToString(CultureInfo.InvariantCulture),
            LongTag @long => @long.Value.ToString(CultureInfo.InvariantCulture),
            ShortTag @short => @short.Value.ToString(CultureInfo.InvariantCulture),
            StringTag @string => @string.Value,
            _ => throw new ArgumentException($"Unsuported tag type '{tag?.GetType().ToString() ?? "null"}'", nameof(tag)),
        };
}
