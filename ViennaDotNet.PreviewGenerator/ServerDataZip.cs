using Cyotek.Data.Nbt;
using Cyotek.Data.Nbt.Serialization;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.PreviewGenerator
{
    internal class ServerDataZip
    {
        public static ServerDataZip Read(Stream inputStream)
        {
            return new ServerDataZip(inputStream);
        }

        private readonly Dictionary<string, byte[]> files = new();

        private ServerDataZip(Stream inputStream)
        {
            using ZipArchive archive = new ZipArchive(inputStream);

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory()) continue;

                using (Stream entryStream = entry.Open())
                using (MemoryStream ms = new MemoryStream())
                {
                    entryStream.CopyTo(ms);
                    files.Add(entry.Name, ms.ToArray());
                }
            }
        }

        public TagCompound getChunkNBT(int x, int z)
        {
            int regionX = x >> 5;
            int regionZ = z >> 5;
            int chunkX = x & 31;
            int chunkZ = z & 31;
            int chunkIndex = (chunkZ << 5) | chunkX;

            using MemoryStream ms = new MemoryStream(files[$"region/r.{regionX}.{regionZ}.mca"]);
            using BinaryReader reader = new BinaryReader(ms);

            ms.Seek(chunkIndex * 4, SeekOrigin.Current);
            int offset = (int)(reader.ReadUInt32() >> 8);

            ms.Seek(offset * 4096, SeekOrigin.Begin);

            int length = (int)reader.ReadUInt32();
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
                        using DeflateStream deflateStream = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress, false);
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
            {
                // false because the stream should now be uncompressed
                BinaryTagReader tagReader = new BinaryTagReader(tagStream, false);
                TagCompound tag = tagReader.ReadDocument();

                return tag;
            }
        }
    }
}
