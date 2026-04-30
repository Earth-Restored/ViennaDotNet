using Serilog;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.PreviewGenerator.Registry;

namespace ViennaDotNet.PreviewGenerator;

public static class Generator
{
    private static readonly int CHUNK_RADIUS = 2;

    public static string Generate(Stream stream)
    {
        //try
        //{
        ServerDataZip serverDataZip = ServerDataZip.Read(stream);

        LinkedList<Chunk> chunks = new();
        for (int chunkX = -CHUNK_RADIUS; chunkX < CHUNK_RADIUS; chunkX++)
        {
            for (int chunkZ = -CHUNK_RADIUS; chunkZ < CHUNK_RADIUS; chunkZ++)
            {
                Chunk? chunk = Chunk.Read(serverDataZip.GetChunkNBT(chunkX, chunkZ));
                if (chunk is null)
                    Log.Error($"Could not convert chunk {chunkX}, {chunkZ}");
                else
                    chunks.AddLast(chunk);
            }
        }

        PreviewModel.SubChunk[] subChunks = chunks
            .SelectMany(chunk =>
            {
                return Java.IntStream.Range(0, 16)
                    .Select(subchunkY =>
                    {
                        Dictionary<int, int> palette = [];
                        int[] blocks = new int[4096];
                        for (int x = 0; x < 16; x++)
                        {
                            for (int y = 0; y < 16; y++)
                            {
                                for (int z = 0; z < 16; z++)
                                {
                                    int blockId = chunk.Blocks[(x * 256 + y + subchunkY * 16) * 16 + z];
                                    blocks[(x * 16 + y) * 16 + z] = palette.ComputeIfAbsent(blockId, blockId1 => palette.Count);
                                }
                            }
                        }

                        if (palette.Count == 1 && palette.ContainsKey(BedrockBlocks.AirId))
                            return null;
                        else
                        {
                            return new PreviewModel.SubChunk(
                                new PreviewModel.Position(chunk.ChunkX, subchunkY, chunk.ChunkZ),
                                [.. palette.Keys
                                    .Select(blockId =>
                                        {
                                            string? name = BedrockBlocks.GetName(blockId) ?? throw new InvalidOperationException();
                                            int data = 0;
                                            while (blockId - data - 1 >= 0 && name == BedrockBlocks.GetName(blockId - data - 1))
                                            {
                                                data++;
                                            }

                                            return new PreviewModel.SubChunk.PaletteEntry(name, data);
                                        })],
                                blocks
                            );
                        }
                    })
                    .Where(subChunk => subChunk is not null);
            })
            .ToArray()!;

        // block entities seem to not be used by the client when rendering the preview anyway?
        // TODO: fix data being empty - client crashes
        PreviewModel.BlockEntity[] blockEntities = [
            // .. chunks
            // .SelectMany(chunk => chunk.BlockEntities)
            // .Where(blockEntity => blockEntity is not null)
            // .Select(blockEntity =>
            // {
            //     int type;
            //     switch (blockEntity!.GetString("id"))
            //     {
            //         case "Bed":
            //             type = 27;
            //             break;
            //         case "PistonArm":
            //             type = 18;
            //             break;
            //         default:
            //             {
            //                 Log.Warning($"No block entity type code mapping for {blockEntity.GetString("id")}");
            //                 type = -1;
            //             }

            //             break;
            //     }

            //     return new PreviewModel.BlockEntity(
            //         type,
            //         new PreviewModel.Position(blockEntity.GetInt("x"), blockEntity.GetInt("y"), blockEntity.GetInt("z")),
            //         JsonNbtConverter.Convert(blockEntity)
            //     );

            // })
            // .Where(blockEntity => blockEntity.Type != -1)
            ];

        // TODO: entities
        PreviewModel previewModel = new PreviewModel(
            1,
            false,
            subChunks,
            blockEntities,
            []
        );

        return Json.Serialize(previewModel);
        //} catch (Exception ex) { }
    }
}
