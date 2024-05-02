using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.PreviewGenerator
{
    public static class Generator
    {
        private static readonly int CHUNK_RADIUS = 2;

        public static void Generate(Stream stream)
        {
            ServerDataZip serverDataZip = ServerDataZip.Read(stream);

            LinkedList<Chunk> chunks = new();
            for (int chunkX = -CHUNK_RADIUS; chunkX < CHUNK_RADIUS; chunkX++)
            {
                for (int chunkZ = -CHUNK_RADIUS; chunkZ < CHUNK_RADIUS; chunkZ++)
                {
                    Chunk chunk = Chunk.read(serverDataZip.getChunkNBT(chunkX, chunkZ));
                    if (chunk == null)
                    {
                        LogManager.getLogger().error("Could not convert chunk {}, {}", chunkX, chunkZ);
                    }
                    else
                    {
                        chunks.add(chunk);
                    }
                }
            }
        }
    }
}
