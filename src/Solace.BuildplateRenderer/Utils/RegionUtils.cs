using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BitcoderCZ.Maths.Vectors;
using SharpNBT;

namespace ViennaDotNet.BuildplateRenderer.Utils;

// https://minecraft.wiki/w/Anvil_file_format
internal static partial class RegionUtils
{
    public const int RegionSize = 32;
    public const int ChunkToLocalMask = RegionSize - 1;

    public const int TimestampOffset = 0x1000;
    public const int HeaderLength = 0x1000 + 0x1000;
    public const int ChunkSize = 0x1000;

    public const byte CompressionTypeGzip = 1;
    public const byte CompressionTypeZlib = 2;
    public const byte CompressionTypeNone = 3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 ChunkToRegion(int2 chunkPosition)
        => new int2(chunkPosition.X >> 5, chunkPosition.Y >> 5);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 ChunkToLocal(int2 chunkPosition)
        => new int2(chunkPosition.X & ChunkToLocalMask, chunkPosition.Y & ChunkToLocalMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int2 LocalToChunk(int2 localPosition, int2 regionPosition)
        => localPosition + (regionPosition * RegionSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LocalToIndex(int2 localPosition)
        => (localPosition.Y << 5) | localPosition.X;

    public static int2 PathToPos(ReadOnlySpan<char> path)
    {
        Debug.Assert(RegionFileRegex().IsMatch(path), $"{nameof(path)} should corespond to a region file.");

        Debug.Assert(path.StartsWith("region/"), $"{nameof(path)} should start with 'region/'");
        path = path[7..];

        Debug.Assert(!path.Contains('/'), $"{nameof(path)} shouldn't contain '/' at this point.");
        Debug.Assert(path.StartsWith("r."), $"{nameof(path)} should start with 'r.' at this point.");
        path = path[2..];

        int dotIndex = path.IndexOf('.');
        Debug.Assert(dotIndex != -1, $"{nameof(path)} should contain '.' at this point.");
        int regionX = int.Parse(path[..dotIndex]);
        path = path[(dotIndex + 1)..];

        dotIndex = path.IndexOf('.');
        Debug.Assert(dotIndex != -1, $"{nameof(path)} should contain '.' at this point.");
        int regionZ = int.Parse(path[..dotIndex]);

        return new int2(regionX, regionZ);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint CalculatePaddedLength(uint chunkDataLength)
    {
        chunkDataLength += 5; // header
        return chunkDataLength % ChunkSize == 0 ? chunkDataLength : chunkDataLength + (ChunkSize - (chunkDataLength % ChunkSize));
    }

    public static bool ContainsChunk(ReadOnlySpan<byte> regionData, int2 localPosition)
    {
        ValidateLocalCoords(localPosition);

        int chunkIndex = LocalToIndex(localPosition);

        int offset = BinaryPrimitives.ReadInt32BigEndian(regionData[(chunkIndex * 4)..]) >> 8;

        return offset >= 2;
    }

    public static IEnumerable<int2> GetChunkPositions(ReadOnlyMemory<byte> regionData)
    {
        for (int z = 0; z < RegionSize; z++)
        {
            for (int x = 0; x < RegionSize; x++)
            {
                var pos = new int2(x, z);
                if (ContainsChunk(regionData.Span, pos))
                {
                    yield return pos;
                }
            }
        }
    }

    public static ReadOnlyMemory<byte> ReadRawChunkData(ReadOnlyMemory<byte> regionData, int2 localPosition, out byte compressionType)
    {
        ValidateLocalCoords(localPosition);

        var dataSpan = regionData.Span;

        Debug.Assert(ContainsChunk(dataSpan, localPosition), $"{nameof(regionData)} should contain a chunk at {localPosition}.");

        int chunkIndex = LocalToIndex(localPosition);

        int offset = (BinaryPrimitives.ReadInt32BigEndian(dataSpan[(chunkIndex * 4)..]) >> 8) * ChunkSize;

        int length = BinaryPrimitives.ReadInt32BigEndian(dataSpan[offset..]) - 1;
        compressionType = dataSpan[offset + 4];

        return regionData.Slice(offset + 5, length);
    }

    /// <exception cref="InvalidDataException">Thrown if the compression type is invalid.</exception>
    public static MemoryStream ReadChunkData(ReadOnlyMemory<byte> regionData, int2 localPosition)
    {
        ValidateLocalCoords(localPosition);

        ReadOnlyMemory<byte> chunkData = ReadRawChunkData(regionData, localPosition, out byte compressionType);

        MemoryStream uncompressed;

        switch (compressionType)
        {
            case CompressionTypeGzip:
                {
                    uncompressed = new MemoryStream(chunkData.Length * 2);

                    using var gZipStream = new GZipStream(new ReadOnlySpanStream(chunkData), CompressionMode.Decompress, false);
                    gZipStream.CopyTo(uncompressed);
                }

                break;
            case CompressionTypeZlib:
                {
                    uncompressed = new MemoryStream(chunkData.Length * 2);

                    using var deflateStream = new ZLibStream(new ReadOnlySpanStream(chunkData), CompressionMode.Decompress, false);
                    deflateStream.CopyTo(uncompressed);
                }

                break;
            case CompressionTypeNone:
                {
                    byte[] buffer = new byte[chunkData.Length];
                    chunkData.CopyTo(buffer.AsMemory());
                    uncompressed = new MemoryStream(buffer);
                    break;
                }

            default:
                throw new InvalidDataException($"Invalid/unknown compression type '{compressionType}'.");
        }

        uncompressed.Position = 0;

        return uncompressed;
    }

    /// <exception cref="InvalidDataException">Thrown if the compression type is invalid.</exception>
    public static CompoundTag ReadChunkNTB(ReadOnlyMemory<byte> regionData, int2 localPosition)
    {
        ValidateLocalCoords(localPosition);

        using (MemoryStream ms = ReadChunkData(regionData, localPosition))
        using (var tagReader = new TagReader(ms, FormatOptions.Java))
        {
            CompoundTag tag = tagReader.ReadTag<CompoundTag>();

            return tag;
        }
    }

    public static void WriteRawChunkData(Span<byte> regionData, Stream chunkData, uint index, byte compressionType, int2 localPosition)
    {
        ValidateLocalCoords(localPosition);

        Debug.Assert(chunkData.CanRead, $"{nameof(chunkData)} should be readable.");
        Debug.Assert(chunkData.CanSeek, $"{nameof(chunkData)} should be seekable.");
        Debug.Assert(index % ChunkSize == 0, $"{nameof(index)} should be a multiple of {nameof(ChunkSize)}.");
        Debug.Assert(index / ChunkSize >= 2, $"{nameof(index)} should be greater than or equal to 2×{nameof(ChunkSize)}.");

        int chunkIndex = LocalToIndex(localPosition);

        uint dataLength = checked((uint)chunkData.Length);
        Debug.Assert(index + dataLength + 5 <= regionData.Length, $"There should be enough space in {nameof(regionData)} to fit {nameof(chunkData)} starting at {index}");
        uint paddedLength = CalculatePaddedLength(dataLength);

        BinaryPrimitives.WriteUInt32BigEndian(regionData[(chunkIndex * 4)..], ((index / ChunkSize) << 8) | paddedLength / ChunkSize);
        BinaryPrimitives.WriteUInt32BigEndian(regionData[((chunkIndex * 4) + TimestampOffset)..], (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        BinaryPrimitives.WriteUInt32BigEndian(regionData[(int)index..], dataLength + 1);
        regionData[(int)index + 4] = compressionType;

        chunkData.Position = 0;
        chunkData.ReadExactly(regionData.Slice((int)index + 5, (int)dataLength));
    }

    public static void WriteChunkNBT(ref byte[] regionData, CompoundTag chunkNBT, int2 localPosition)
    {
        ValidateLocalCoords(localPosition);

        using var ms = new MemoryStream();
        using var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize);
        using var writer = new TagWriter(zlib, FormatOptions.Java);

        // for some reason if the name is empty, the type doesn't get written... wtf, also in this case an empty name is expected
        // compound type
        zlib.WriteByte(10);

        // name length
        Debug.Assert(string.IsNullOrEmpty(chunkNBT.Name), $"{nameof(chunkNBT)}.Name should be null or empty.");
        zlib.WriteByte(0);
        zlib.WriteByte(0);

        writer.WriteTag(chunkNBT);
        zlib.Flush();

        uint dataLength = checked((uint)ms.Length);
        uint paddedLength = CalculatePaddedLength(dataLength);

        uint index;
        if (regionData.Length == 0)
        {
            regionData = new byte[HeaderLength + paddedLength];
            index = HeaderLength;
        }
        else
        {
            byte[] newRegionData = new byte[regionData.Length + paddedLength];
            Buffer.BlockCopy(regionData, 0, newRegionData, 0, regionData.Length);

            index = (uint)regionData.Length;

            regionData = newRegionData;
        }

        WriteRawChunkData(regionData, ms, index, CompressionTypeZlib, localPosition);
    }

    [Conditional("DEBUG")]
    private static void ValidateLocalCoords(int2 localPosition)
    {
        Debug.Assert(localPosition.X is >= 0 and < RegionSize, $"{nameof(localPosition)}.X must be in bounds.");
        Debug.Assert(localPosition.Y is >= 0 and < RegionSize, $"{nameof(localPosition)}.Y must be in bounds.");
    }

    [GeneratedRegex(@"^region/r\.-?\d+\.-?\d+\.mca$")]
    private static partial Regex RegionFileRegex();
}
