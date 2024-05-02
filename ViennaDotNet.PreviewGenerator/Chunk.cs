using Cyotek.Data.Nbt;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ViennaDotNet.PreviewGenerator.Registry;
using ViennaDotNet.PreviewGenerator.Utils.NBT;

namespace ViennaDotNet.PreviewGenerator
{
    internal class Chunk
    {
        public static Chunk? read(TagCompound chunkTag)
        {
            try
            {
                return new Chunk(chunkTag);
            }
            catch (Exception exception)
            {
                Log.Error($"Could not read chunk: {exception}");
                return null;
            }
        }

        public readonly int chunkX;
        public readonly int chunkZ;

        public readonly int[] blocks = new int[16 * 256 * 16];
        public readonly NbtMap?[] blockEntities = new NbtMap[16 * 256 * 16];

        private Chunk(TagCompound chunkTag)
        {
            chunkX = chunkTag.GetIntValue("xPos");
            chunkZ = chunkTag.GetIntValue("zPos");

            JavaBlocks.BedrockMapping.BlockEntity?[] blockEntityMappings = new JavaBlocks.BedrockMapping.BlockEntity[16 * 256 * 16];
            JavaBlocks.BedrockMapping.ExtraData?[] extraDatas = new JavaBlocks.BedrockMapping.ExtraData[16 * 256 * 16];

            Array.Fill(blocks, BedrockBlocks.AIR);
            Array.Fill(blockEntities, null);
            Array.Fill(blockEntityMappings, null);
            Array.Fill(extraDatas, null);

            HashSet<string> alreadyNotifiedMissingBlocks = new();
            for (int subchunkY = 0; subchunkY < 16; subchunkY++)
            {
                int sectionIndex = subchunkY + 4 + 1; // Java world height starts at -64, plus one section for bottommost lighting
                TagCompound sectionTag = (TagCompound)(chunkTag.GetList("sections")).Value[sectionIndex];

                TagCompound blockStatesTag = sectionTag.GetCompound("block_states");

                TagList paletteTag = blockStatesTag.GetList("palette");
                List<string> javaPalette = new(paletteTag.Count);
                foreach (Tag paletteEntryTag in paletteTag.Value)
                {
                    javaPalette.Add(readPaletteEntry((TagCompound)paletteEntryTag));
                }

                int[] javaBlocks;
                if (javaPalette.Count == 0)
                    throw new IOException("Chunk section has empty palette");

                if (!blockStatesTag.Contains("data"))
                {
                    if (javaPalette.Count > 1)
                        throw new IOException("Chunk section has palette with more than one entry and no data");

                    javaBlocks = new int[4096];
                    Array.Fill(javaBlocks, 0);
                }
                else
                    javaBlocks = readBitArray(blockStatesTag.get("data"), javaPalette.Count);

                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y < 16; y++)
                    {
                        for (int z = 0; z < 16; z++)
                        {
                            string javaName = javaPalette[javaBlocks[(y * 16 + z) * 16 + x]];

                            JavaBlocks.BedrockMapping? bedrockMapping = JavaBlocks.getBedrockMapping(javaName);
                            if (bedrockMapping == null)
                            {
                                if (alreadyNotifiedMissingBlocks.Add(javaName))
                                    Log.Warning($"Chunk contained block with no mapping {javaName}");
                            }

                            // TODO: how to handle waterlogged blocks???
                            int bedrockId = bedrockMapping != null ? bedrockMapping.id : BedrockBlocks.AIR;
                            blocks[(x * 256 + (y + subchunkY * 16)) * 16 + z] = bedrockId;

                            JavaBlocks.BedrockMapping.BlockEntity? blockEntityMapping = bedrockMapping != null && bedrockMapping.blockEntity != null ? bedrockMapping.blockEntity : null;
                            NbtMap bedrockBlockEntityData = blockEntityMapping != null ? BlockEntityTranslator.translateBlockEntity(blockEntityMapping, null) : null;
                            if (bedrockBlockEntityData != null)
                                bedrockBlockEntityData = bedrockBlockEntityData.toBuilder().putInt("x", x + chunkX * 16).putInt("y", y + subchunkY * 16).putInt("z", z + chunkZ * 16).putBoolean("isMovable", false).build();

                            blockEntities[(x * 256 + (y + subchunkY * 16)) * 16 + z] = bedrockBlockEntityData;
                            blockEntityMappings[(x * 256 + (y + subchunkY * 16)) * 16 + z] = blockEntityMapping;

                            extraDatas[(x * 256 + (y + subchunkY * 16)) * 16 + z] = bedrockMapping != null ? bedrockMapping.extraData : null;
                        }
                    }
                }
            }

            foreach (Tag blockEntityTag in chunkTag.GetList("block_entities").Value)
            {
                TagCompound blockEntityCompoundTag = (TagCompound)blockEntityTag;
                int x = getChunkBlockOffset((blockEntityCompoundTag.GetInt("x")).Value);
                int y = (blockEntityCompoundTag.GetInt("y")).Value;
                int z = getChunkBlockOffset((blockEntityCompoundTag.GetInt("z")).Value);
                string type = (blockEntityCompoundTag.GetString("id")).Value;
                BlockEntityInfo blockEntityInfo = new BlockEntityInfo(x, y, z, BlockEntityType.FURNACE, blockEntityCompoundTag);    // TODO: use proper type (currently this doesn't matter for any of our translator implementations)

                JavaBlocks.BedrockMapping.BlockEntity? blockEntityMapping = blockEntityMappings[(x * 256 + y) * 16 + z];
                if (blockEntityMapping == null)
                    Log.Debug($"Ignoring block entity of type {type}");

                NbtMap bedrockBlockEntityData = blockEntityMapping != null ? BlockEntityTranslator.translateBlockEntity(blockEntityMapping, blockEntityInfo) : null;
                if (bedrockBlockEntityData != null)
                    bedrockBlockEntityData = bedrockBlockEntityData.toBuilder().putInt("x", x + chunkX * 16).putInt("y", y).putInt("z", z + chunkZ * 16).putBoolean("isMovable", false).build();

                blockEntities[(x * 256 + y) * 16 + z] = bedrockBlockEntityData;
            }
        }

        // TODO: this relies on the state tags in the block names in the Java blocks registry matching the actual server names/values and to be sorted in alphabetical order, should verify/ensure that this is the case
        private static string readPaletteEntry(TagCompound paletteEntryTag)
        {
            string name = (paletteEntryTag.GetString("Name")).Value;

            List<string> properties = new();
            if (paletteEntryTag.Contains("Properties"))
            {
                foreach (Tag propertyTag in paletteEntryTag.GetCompound("Properties").Value)
                    properties.Add(propertyTag.Name + "=" + propertyTag.GetValue());
            }

            properties.Sort(string.Compare);

            if (properties.Count > 0)
            {
                name = name + "[" + string.Join(",", properties.ToArray()) + "]";
            }

            return name;
        }

        private static int[] readBitArray(@NotNull LongArrayTag longArrayTag, int maxValue) throws Exception
        {

        int[] out = new int[4096];
int outIndex = 0;

        long[] in = longArrayTag.getValue();
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

long currentIn = in[inIndex++] ;
inSubIndex = 0;
while (outIndex < out.length)
		{
			if (inSubIndex >= valuesPerLong)
			{
				currentIn = in[inIndex++] ;
inSubIndex = 0;
			}
			long value = (currentIn >> ((inSubIndex++) * bits)) & ((1 << bits) - 1);
			out[outIndex++] = (int)value;
		}

		return out;
	}

	private static int getChunkBlockOffset(int pos)
{
    return pos >= 0 ? pos % 16 : 15 - ((-pos - 1) % 16);
}
    }
}
