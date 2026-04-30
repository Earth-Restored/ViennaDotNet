using SharpNBT;
using System.IO.Compression;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.PreviewGenerator;

internal sealed class ServerDataZip
{
    public static ServerDataZip Read(Stream inputStream)
        => new ServerDataZip(inputStream);

    private readonly Dictionary<string, byte[]> _files = [];

    private ServerDataZip(Stream inputStream)
    {
        using ZipArchive archive = new ZipArchive(inputStream);

        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;

            using (Stream entryStream = entry.Open())
            using (MemoryStream ms = new MemoryStream())
            {
                entryStream.CopyTo(ms);
                _files.Add(entry.FullName, ms.ToArray());
            }
        }
    }

    public CompoundTag GetChunkNBT(int x, int z)
    {
        int regionX = x >> 5;
        int regionZ = z >> 5;
        int chunkX = x & 31;
        int chunkZ = z & 31;
        int chunkIndex = (chunkZ << 5) | chunkX;

        using MemoryStream ms = new MemoryStream(_files[$"region/r.{regionX}.{regionZ}.mca"]);
        using BinaryReader reader = new BinaryReader(ms);

        ms.Seek(chunkIndex * 4, SeekOrigin.Begin);
        int offset = (int)(reader.ReadUInt32BE() >> 8);

        ms.Seek(offset * 4096, SeekOrigin.Begin);

        int length = (int)reader.ReadUInt32BE();
        byte compressionType = reader.ReadByte();
        byte[] compressed = new byte[length];
        ms.Read(compressed);
        byte[] uncompressed;
        switch (compressionType)
        {
            case 1:
                {
                    using GZipStream gZipStream = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress, false);
                    using MemoryStream resultStream = new MemoryStream();
                    gZipStream.CopyTo(resultStream);
                    uncompressed = resultStream.ToArray();
                }

                break;
            case 2:
                {
                    using ZLibStream deflateStream = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress, false);
                    using MemoryStream resultStream = new MemoryStream();
                    deflateStream.CopyTo(resultStream);
                    uncompressed = resultStream.ToArray();
                }

                break;
            case 3:
                {
                    uncompressed = compressed;
                    break;
                }
            default:
                throw new IOException($"Invalid compression type {compressionType}");
        }

        using (MemoryStream tagStream = new MemoryStream(uncompressed))
        using (TagReader tagReader = new TagReader(tagStream, FormatOptions.Java, false))
        {
            CompoundTag tag = tagReader.ReadTag<CompoundTag>();

            return tag;
        }
    }
}
