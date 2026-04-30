// <copyright file="ChunkUtils.cs" company="BitcoderCZ">
// Copyright (c) BitcoderCZ. All rights reserved.
// </copyright>

using System.Buffers;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using BitcoderCZ.Maths.Vectors;
using SharpNBT;
using ViennaDotNet.BuildplateRenderer.Models.ResourcePacks;

namespace ViennaDotNet.BuildplateRenderer.Utils;

internal static class ChunkUtils
{
	public const int Width = 16;
	public const int Height = 256;
	public const int SubChunkSize = 16;

	public static readonly int[] EmptySubChunk = new int[Width * SubChunkSize * Width];

	public static readonly FrozenSet<string> InvisibleBlocks = new HashSet<string>()
	{
		"minecraft:air",
		"fountain:solid_air",
		"fountain:non_replaceable_air",
		"fountain:invisible_constraint",
		"fountain:blend_constraint",
		"fountain:border_constraint",
	}.ToFrozenSet(StringComparer.Ordinal);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int2 BlockToChunk(int2 blockPosition)
		=> new int2(blockPosition.X >> 4, blockPosition.Y >> 4);

	public static int[] ReadBlockData(LongArrayTag nbt)
	{
		if (nbt.Count == 0)
		{
			return EmptySubChunk;
		}

		var resultData = GC.AllocateUninitializedArray<int>(Width * SubChunkSize * Width);

		var longArray = nbt.Span;

		int bits = 4;

		for (int b = 4; b <= 64; b++)
		{
			int vpl = 64 / b;
			int expectedLength = (4096 + vpl - 1) / vpl;

			if (expectedLength == longArray.Length)
			{
				bits = b;
				break;
			}
		}

		int valuesPerLong = 64 / bits;
		long mask = (1L << bits) - 1;

		int dataIndex = 0;

		for (int i = 0; i < longArray.Length; i++)
		{
			long value = longArray[i];

			for (int j = 0; j < valuesPerLong; j++)
			{
				if (dataIndex >= 4096)
				{
					break;
				}

				resultData[dataIndex++] = (int)((value >> (j * bits)) & mask);
			}
		}

		return resultData;
	}

	public static BlockState? TagToBlockStateVisibleFromPool(CompoundTag paletteEntry)
	{
		string blockName = ((StringTag)paletteEntry["Name"]).Value;

		if (InvisibleBlocks.Contains(blockName))
		{
			return null;
		}
		
		if (blockName is "minecraft:water" or "minecraft:lava")
		{
			// TODO:
			return null;
		}

		var propertiesArray = ArrayPool<KeyValuePair<string, string>>.Shared.Rent(64);
		int propertiesArrayLength = 0;
		if (paletteEntry.TryGetValue("Properties", out var propertiesTag))
		{
			foreach (var item in (ICollection<KeyValuePair<string, Tag>>)(CompoundTag)propertiesTag)
			{
				if (propertiesArrayLength >= propertiesArray.Length)
				{
					ArrayPool<KeyValuePair<string, string>>.Shared.Return(propertiesArray);
					propertiesArray = ArrayPool<KeyValuePair<string, string>>.Shared.Rent(propertiesArray.Length * 2);
				}

				propertiesArray[propertiesArrayLength++] = new(item.Key, ((StringTag)item.Value).Value);
			}
		}

		var blockState = BlockState.CreateNoCopy(blockName, propertiesArray, propertiesArrayLength);

		return blockState;
	}
}
