using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using BitcoderCZ.Maths.Vectors;
using SharpNBT;
using ViennaDotNet.Buildplate.Model;
using ViennaDotNet.BuildplateRenderer.Models.ResourcePacks;
using ViennaDotNet.BuildplateRenderer.Utils;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.BuildplateRenderer;

[StructLayout(LayoutKind.Sequential)]
public struct MeshVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public int TintIndex;
}

public sealed class MeshPrimitive
{
    public List<MeshVertex> Vertices { get; } = [];
    public List<int> Indices { get; } = [];
}

public class MeshData
{
    // Grouped by texture
    public Dictionary<string, MeshPrimitive> Primitives { get; } = [];
}

internal sealed class MeshGenerator
{
    private static readonly FrozenSet<string> InvisibleBlocks = new HashSet<string>()
    {
        "minecraft:air",
        "fountain:solid_air",
        "fountain:non_replaceable_air",
        "fountain:invisible_constraint",
        "fountain:blend_constraint",
        "fountain:border_constraint",
    }.ToFrozenSet(StringComparer.Ordinal);


    private const float BlockModelScale = 1f / 16f;

    private readonly ResourcePack _resourcePack;
    private readonly Random _rng = new();

    public MeshGenerator(ResourcePack resourcePack)
    {
        _resourcePack = resourcePack;
    }

    public async Task<MeshData> GenerateAsync(WorldData worldData, CancellationToken cancellationToken = default)
    {
        var mesh = new MeshData();

        using (var serverDataStream = new MemoryStream(worldData.ServerData))
        using (var zip = await ZipArchive.CreateAsync(serverDataStream, ZipArchiveMode.Read, false, null, cancellationToken))
        {
            foreach (var entry in zip.Entries)
            {
                if (!entry.IsDirectory && entry.FullName.StartsWith("region"))
                {
                    var entryStream = await entry.OpenAsync(cancellationToken);
                    byte[] regionData = GC.AllocateUninitializedArray<byte>(checked((int)entry.Length));
                    await entryStream.ReadExactlyAsync(regionData, cancellationToken);

                    ProcessRegion(regionData, RegionUtils.PathToPos(entry.FullName), mesh);
                }
            }
        }

        return mesh;
    }

    private void ProcessRegion(byte[] regionData, int2 regionPosition, MeshData mesh)
    {
        foreach (var localPosition in RegionUtils.GetChunkPositions(regionData))
        {
            var chunkNBT = RegionUtils.ReadChunkNTB(regionData, localPosition);

            ProcessChunk(chunkNBT, RegionUtils.LocalToChunk(localPosition, regionPosition), mesh);
        }
    }

    // https://minecraft.wiki/w/Chunk_format
    private void ProcessChunk(CompoundTag nbt, int2 chunkPosition, MeshData mesh)
    {
        Debug.Assert(((IntTag)nbt["xPos"]).Value == chunkPosition.X);
        Debug.Assert(((IntTag)nbt["zPos"]).Value == chunkPosition.Y);

        foreach (var item in (ListTag)nbt["sections"])
        {
            var subChunkNBT = (CompoundTag)item;
            if (!subChunkNBT.ContainsKey("block_states"))
            {
                continue;
            }

            ProcessSubChunk(subChunkNBT, new int3(chunkPosition.X, ((ByteTag)subChunkNBT["Y"]).Value, chunkPosition.Y), mesh);
        }
    }

    private void ProcessSubChunk(CompoundTag nbt, int3 chunkPosition, MeshData mesh)
    {
        var blockStates = (CompoundTag)nbt["block_states"];

        var palette = (ListTag)blockStates["palette"];

        bool foundVisibleBlock = false;
        foreach (var entry in palette)
        {
            if (!InvisibleBlocks.Contains(((StringTag)((CompoundTag)entry)["Name"]).Value))
            {
                foundVisibleBlock = true;
                break;
            }
        }

        if (!foundVisibleBlock)
        {
            return;
        }

        var blocks = blockStates.ContainsKey("data")
            ? ChunkUtils.ReadBlockData((LongArrayTag)blockStates["data"])
            : ChunkUtils.EmptySubChunk;

        var blockPosition = int3.Zero;

        var propertiesArray = ArrayPool<KeyValuePair<string, string>>.Shared.Rent(64);
        var modelVariants = ArrayPool<VariantModel>.Shared.Rent(64);

        foreach (var blockIndex in blocks)
        {
            Debug.Assert(blockPosition.X is >= 0 and < ChunkUtils.Width);
            Debug.Assert(blockPosition.Y is >= 0 and < ChunkUtils.SubChunkHeight);
            Debug.Assert(blockPosition.Z is >= 0 and < ChunkUtils.Width);

            var paletteEntry = (CompoundTag)palette[blockIndex];

            string blockName = ((StringTag)paletteEntry["Name"]).Value;

            if (!InvisibleBlocks.Contains(blockName))
            {
                if (blockName is "minecraft:water" or "minecraft:lava")
                {
                    // TODO:
                    continue;
                }

                int propertiesArrayLength = 0;
                if (paletteEntry.TryGetValue("Properties", out var propertiesTag))
                {
                    foreach (var item in (ICollection<KeyValuePair<string, Tag>>)(CompoundTag)propertiesTag)
                    {
                        if (item.Key is "waterlogged")
                        {
                            continue;
                        }

                        if (propertiesArrayLength >= propertiesArray.Length)
                        {
                            ArrayPool<KeyValuePair<string, string>>.Shared.Return(propertiesArray);
                            propertiesArray = ArrayPool<KeyValuePair<string, string>>.Shared.Rent(propertiesArray.Length * 2);
                        }

                        propertiesArray[propertiesArrayLength++] = new(item.Key, ((StringTag)item.Value).Value);
                    }
                }

                // todo: non allocating ctor, add a short PropertiesLength field
                var blockState = new Models.ResourcePacks.BlockState(blockName, propertiesArray.AsSpan()[..propertiesArrayLength]);

                var modelVariantsLength = _resourcePack.GetModelVariant(blockState, _rng, modelVariants);
                foreach (var modelVariant in modelVariants.AsSpan(0, modelVariantsLength))
                {
                    GenerateBlockMesh(modelVariant, chunkPosition + blockPosition, mesh);
                }
            }

            blockPosition.X++;
            if (blockPosition.X >= ChunkUtils.Width)
            {
                blockPosition.X = 0;
                blockPosition.Z++;
                if (blockPosition.Z >= 16)
                {
                    blockPosition.Z = 0;
                    blockPosition.Y++;
                }
            }
        }

        ArrayPool<KeyValuePair<string, string>>.Shared.Return(propertiesArray);
        ArrayPool<VariantModel>.Shared.Return(modelVariants);
    }

    private void GenerateBlockMesh(VariantModel modelVariant, int3 blockPosition, MeshData mesh)
    {
        var model = _resourcePack.GetBlockModel(modelVariant.Model);

        Matrix4x4 variantTransform = CreateVariantTransform(modelVariant);

        foreach (var element in model.Elements)
        {
            Vector3 from = element.From * BlockModelScale;
            Vector3 to = element.To * BlockModelScale;

            Matrix4x4 elementTransform = CreateElementTransform(element.Rotation);
            Matrix4x4 finalTransform = elementTransform * variantTransform;

            for (int i = 0; i < 6; i++)
            {
                var direction = (Direcion)i;

                BlockFace? face = element.Faces[(int)direction];

                if (face is null)
                {
                    continue;
                }

                string actualTexture = face.Texture;
                while (actualTexture.StartsWith('#') && model.Textures is not null)
                {
                    model.Textures.TryGetValue(actualTexture[1..], out actualTexture!);
                }

                if (!mesh.Primitives.TryGetValue(actualTexture, out var primitive))
                {
                    primitive = new MeshPrimitive();
                    mesh.Primitives[actualTexture] = primitive;
                }

                BuildFace(blockPosition, direction, from, to, face, finalTransform, modelVariant.UVLock, primitive);
            }
        }
    }

    private static void BuildFace(Vector3 blockPosition, Direcion dir, Vector3 from, Vector3 to, BlockFace face, Matrix4x4 transform, bool uvLock, MeshPrimitive primitive)
    {
        int startIndex = primitive.Vertices.Count;

        Span<Vector3> corners = stackalloc Vector3[4];
        GetFaceVertices(dir, from, to, corners, out Vector3 normal);

        Span<Vector2> uvs = stackalloc Vector2[4];
        CalculateUVs(face.UV, face.Rotation, uvs);

        for (int i = 0; i < 4; i++)
        {
            var pos = blockPosition + Vector3.Transform(corners[i], transform);

            var norm = Vector3.Normalize(Vector3.TransformNormal(normal, transform));

            primitive.Vertices.Add(new MeshVertex
            {
                Position = pos,
                Normal = norm,
                UV = uvs[i],
                TintIndex = face.TintIndex
            });
        }

        primitive.Indices.Add(startIndex + 0);
        primitive.Indices.Add(startIndex + 1);
        primitive.Indices.Add(startIndex + 2);
        primitive.Indices.Add(startIndex + 2);
        primitive.Indices.Add(startIndex + 3);
        primitive.Indices.Add(startIndex + 0);
    }

    private static Matrix4x4 CreateElementTransform(BlockElementRotation? rot)
    {
        if (!rot.HasValue)
        {
            return Matrix4x4.Identity;
        }

        var r = rot.Value;
        Vector3 origin = r.Origin * BlockModelScale;

        // Convert degrees to radians
        float radX = r.X * (MathF.PI / 180f);
        float radY = r.Y * (MathF.PI / 180f);
        float radZ = r.Z * (MathF.PI / 180f);

        Matrix4x4 matrix = Matrix4x4.Identity;

        // Move to Origin
        matrix *= Matrix4x4.CreateTranslation(-origin);

        // Rotate
        matrix *= Matrix4x4.CreateRotationX(radX)
                * Matrix4x4.CreateRotationY(radY)
                * Matrix4x4.CreateRotationZ(radZ);

        // Apply Rescaling (Minecraft scales faces across the block to prevent Z-fighting/clipping)
        if (r.ReScale)
        {
            float scaleX = r.X != 0 ? 1f / MathF.Cos(radX) : 1f;
            float scaleY = r.Y != 0 ? 1f / MathF.Cos(radY) : 1f;
            float scaleZ = r.Z != 0 ? 1f / MathF.Cos(radZ) : 1f;
            matrix *= Matrix4x4.CreateScale(scaleX, scaleY, scaleZ);
        }

        // Move back from Origin
        matrix *= Matrix4x4.CreateTranslation(origin);

        return matrix;
    }

    private static Matrix4x4 CreateVariantTransform(VariantModel variant)
    {
        if (variant is { RotationX: 0, RotationY: 0, RotationZ: 0 })
        {
            return Matrix4x4.Identity;
        }

        var center = new Vector3(0.5f, 0.5f, 0.5f);

        float radX = variant.RotationX * (MathF.PI / 180f);
        float radY = variant.RotationY * (MathF.PI / 180f);
        float radZ = variant.RotationZ * (MathF.PI / 180f);

        return Matrix4x4.CreateTranslation(-center)
             * Matrix4x4.CreateRotationX(radX)
             * Matrix4x4.CreateRotationY(radY)
             * Matrix4x4.CreateRotationZ(radZ)
             * Matrix4x4.CreateTranslation(center);
    }

    private static void GetFaceVertices(Direcion dir, Vector3 from, Vector3 to, Span<Vector3> corners, out Vector3 normal)
    {
        Debug.Assert(corners.Length is 4);

        // Z may need to be flipped
        switch (dir)
        {
            case Direcion.Up: // +Y
                normal = Vector3.UnitY;
                corners[0] = new Vector3(from.X, to.Y, from.Z);
                corners[1] = new Vector3(from.X, to.Y, to.Z);
                corners[2] = new Vector3(to.X, to.Y, to.Z);
                corners[3] = new Vector3(to.X, to.Y, from.Z);
                break;
            case Direcion.Down: // -Y
                normal = -Vector3.UnitY;
                corners[0] = new Vector3(from.X, from.Y, to.Z);
                corners[1] = new Vector3(from.X, from.Y, from.Z);
                corners[2] = new Vector3(to.X, from.Y, from.Z);
                corners[3] = new Vector3(to.X, from.Y, to.Z);
                break;
            case Direcion.East: // +X
                normal = Vector3.UnitX;
                corners[0] = new Vector3(to.X, to.Y, to.Z);
                corners[1] = new Vector3(to.X, from.Y, to.Z);
                corners[2] = new Vector3(to.X, from.Y, from.Z);
                corners[3] = new Vector3(to.X, to.Y, from.Z);
                break;
            case Direcion.West: // -X
                normal = -Vector3.UnitX;
                corners[0] = new Vector3(from.X, to.Y, from.Z);
                corners[1] = new Vector3(from.X, from.Y, from.Z);
                corners[2] = new Vector3(from.X, from.Y, to.Z);
                corners[3] = new Vector3(from.X, to.Y, to.Z);
                break;
            case Direcion.North: // -Z
                normal = -Vector3.UnitZ;
                corners[0] = new Vector3(to.X, to.Y, from.Z);
                corners[1] = new Vector3(to.X, from.Y, from.Z);
                corners[2] = new Vector3(from.X, from.Y, from.Z);
                corners[3] = new Vector3(from.X, to.Y, from.Z);
                break;
            case Direcion.South: // +Z
                normal = Vector3.UnitZ;
                corners[0] = new Vector3(from.X, to.Y, to.Z);
                corners[1] = new Vector3(from.X, from.Y, to.Z);
                corners[2] = new Vector3(to.X, from.Y, to.Z);
                corners[3] = new Vector3(to.X, to.Y, to.Z);
                break;
            default:
                normal = Vector3.Zero;
                break;
        }
    }

    private static void CalculateUVs(UVCoordinates uv, int rotation, Span<Vector2> result)
    {
        Debug.Assert(result.Length is 4);

        // Scale 0-16 to 0-1.
        float u0 = uv.Min.X * BlockModelScale;
        float v0 = uv.Min.Y * BlockModelScale;
        float u1 = uv.Max.X * BlockModelScale;
        float v1 = uv.Max.Y * BlockModelScale;

        // top-left, bottom-left, bottom-right, top-right
        result[0] = new Vector2(u0, v0);
        result[1] = new Vector2(u0, v1);
        result[2] = new Vector2(u1, v1);
        result[3] = new Vector2(u1, v0);

        // If rotation is applied (90, 180, 270), shift the array
        if (rotation != 0)
        {
            int shifts = (rotation / 90) % 4;
            if (shifts is 1)
            {
                var tmp = result[0];
                result[0] = result[1];
                result[1] = result[2];
                result[2] = result[3];
                result[3] = tmp;
            }
            else if (shifts is 2)
            {
                var tmp0 = result[0];
                var tmp1 = result[1];
                result[0] = result[2];
                result[1] = result[3];
                result[2] = tmp0;
                result[3] = tmp1;
            }
            else if (shifts is 3)
            {
                var tmp = result[3];
                result[3] = result[2];
                result[2] = result[1];
                result[1] = result[0];
                result[0] = tmp;
            }
        }
    }
}