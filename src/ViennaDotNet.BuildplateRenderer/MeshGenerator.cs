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
public readonly struct MeshVertex
{
    public readonly Vector3 Position;
    public readonly Vector3 Normal;
    public readonly Vector2 UV;
    public readonly int TintIndex;

    public MeshVertex(Vector3 position, Vector3 normal, Vector2 uV, int tintIndex)
    {
        Position = position;
        Normal = normal;
        UV = uV;
        TintIndex = tintIndex;
    }
}

public sealed class MeshPrimitive
{
    public List<MeshVertex> Vertices { get; } = [];
    public List<int> Indices { get; } = [];
}

public sealed class MeshData
{
    // Grouped by texture
    public Dictionary<string, MeshPrimitive> Primitives { get; } = [];
}

public sealed class BuildplateMeshGenerator
{
    private const float BlockModelScale = 1f / 16f;

    private readonly ResourcePackManager _resourcePack;
    private readonly Random _rng = new();

    public BuildplateMeshGenerator(ResourcePackManager resourcePack)
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

                    ProcessRegion(regionData, RegionUtils.PathToPos(entry.FullName), mesh, new int3(0, -worldData.Offset / 2, 0));
                }
            }
        }

        return mesh;
    }

    private void ProcessRegion(byte[] regionData, int2 regionPosition, MeshData mesh, int3 offset)
    {
        foreach (var localPosition in RegionUtils.GetChunkPositions(regionData))
        {
            var chunkNBT = RegionUtils.ReadChunkNTB(regionData, localPosition);

            ProcessChunk(chunkNBT, RegionUtils.LocalToChunk(localPosition, regionPosition), mesh, offset);
        }
    }

    // https://minecraft.wiki/w/Chunk_format
    private void ProcessChunk(CompoundTag nbt, int2 chunkPosition, MeshData mesh, int3 offset)
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

            ProcessSubChunk(subChunkNBT, new int3(chunkPosition.X, ((ByteTag)subChunkNBT["Y"]).Value, chunkPosition.Y), mesh, offset);
        }
    }

    private void ProcessSubChunk(CompoundTag nbt, int3 chunkPosition, MeshData mesh, int3 offset)
    {
        var blockStates = (CompoundTag)nbt["block_states"];

        var palette = (ListTag)blockStates["palette"];

        bool foundVisibleBlock = false;
        foreach (var entry in palette)
        {
            if (!ChunkUtils.InvisibleBlocks.Contains(((StringTag)((CompoundTag)entry)["Name"]).Value))
            {
                foundVisibleBlock = true;
                break;
            }
        }

        if (!foundVisibleBlock)
        {
            return;
        }

        var chunkBlockPosition = chunkPosition * ChunkUtils.SubChunkSize;

        var blocks = blockStates.ContainsKey("data")
            ? ChunkUtils.ReadBlockData((LongArrayTag)blockStates["data"])
            : ChunkUtils.EmptySubChunk;

        var blockPosition = int3.Zero;

        var propertiesArray = ArrayPool<KeyValuePair<string, string>>.Shared.Rent(64);
        var modelVariants = ArrayPool<VariantModel>.Shared.Rent(64);

        foreach (var blockIndex in blocks)
        {
            Debug.Assert(blockPosition.X is >= 0 and < ChunkUtils.Width);
            Debug.Assert(blockPosition.Y is >= 0 and < ChunkUtils.SubChunkSize);
            Debug.Assert(blockPosition.Z is >= 0 and < ChunkUtils.Width);

            var paletteEntry = (CompoundTag)palette[blockIndex];

            string blockName = ((StringTag)paletteEntry["Name"]).Value;

            if (!ChunkUtils.InvisibleBlocks.Contains(blockName))
            {
                if (blockName is "minecraft:water" or "minecraft:lava")
                {
                    // TODO:
                    goto incrementPos;
                }

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

                var modelVariantsLength = _resourcePack.GetModelVariants(blockState, _rng, modelVariants);
                foreach (var modelVariant in modelVariants.AsSpan(0, modelVariantsLength))
                {
                    GenerateBlockMesh(modelVariant, chunkBlockPosition + blockPosition + offset, mesh, blockPosition =>
                    {
                        var localPosition = blockPosition - chunkBlockPosition;

                        if (!localPosition.InBounds(ChunkUtils.SubChunkSize, ChunkUtils.SubChunkSize, ChunkUtils.SubChunkSize))
                        {
                            return null;
                        }

                        return ChunkUtils.TagToBlockStateVisibleFromPool((CompoundTag)palette[blocks[localPosition.X + localPosition.Z * ChunkUtils.Width + localPosition.Y * ChunkUtils.Width * ChunkUtils.Width]]);
                    }, blockState => ArrayPool<KeyValuePair<string, string>>.Shared.Return(blockState._properties));
                }
            }

            incrementPos:
            blockPosition.X++;
            if (blockPosition.X >= ChunkUtils.Width)
            {
                blockPosition.X = 0;
                blockPosition.Z++;
                if (blockPosition.Z >= ChunkUtils.Width)
                {
                    blockPosition.Z = 0;
                    blockPosition.Y++;
                }
            }
        }

        Debug.Assert(blockPosition == new int3(0, ChunkUtils.SubChunkSize, 0));

        ArrayPool<KeyValuePair<string, string>>.Shared.Return(propertiesArray);
        ArrayPool<VariantModel>.Shared.Return(modelVariants);
    }

    private void GenerateBlockMesh(VariantModel modelVariant, int3 blockPosition, MeshData mesh, Func<int3, BlockState?> getBlockAtPos, Action<BlockState> disposeBlockState)
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
                var direction = (Direction)i;

                BlockFace? face = element.Faces[(int)direction];

                if (face is null)
                {
                    continue;
                }

                if (face.CullFace.HasValue)
                {
                    // Rotate the defined cull direction based on the variant's transform
                    Vector3 cullNormal = GetDirectionVector3(face.CullFace.Value);
                    var rotatedNormal = Vector3.TransformNormal(cullNormal, variantTransform);
                    Direction actualCullDir = GetClosestDirection(rotatedNormal);

                    int3 neighborPos = blockPosition + GetDirectionOffset(actualCullDir);

                    var neighbor = getBlockAtPos(neighborPos);
                    if (neighbor is not null)
                    {
                        // todo: compute faceGrid for this face too, cull if they are equal
                        if (IsBlockFullAndOpaque(neighbor.Value, (Direction)((int)actualCullDir ^ 1)))
                        {
                            disposeBlockState(neighbor.Value);
                            continue;
                        }

                        disposeBlockState(neighbor.Value);
                    }
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

    private static void BuildFace(Vector3 blockPosition, Direction dir, Vector3 from, Vector3 to, BlockFace face, Matrix4x4 transform, bool uvLock, MeshPrimitive primitive)
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

            primitive.Vertices.Add(new MeshVertex(pos, norm, uvs[i], face.TintIndex));
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
        matrix *= CreateMinecraftRotation(r.X, r.Y, r.Z);

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

        return Matrix4x4.CreateTranslation(-center)
             * CreateMinecraftRotation(variant.RotationX, variant.RotationY, variant.RotationZ)
             * Matrix4x4.CreateTranslation(center);
    }

    private static Matrix4x4 CreateMinecraftRotation(float degreesX, float degreesY, float degreesZ)
    {
        float radX = degreesX * (MathF.PI / 180f);
        float radY = -degreesY * (MathF.PI / 180f); 
        float radZ = degreesZ * (MathF.PI / 180f);

        return Matrix4x4.CreateRotationY(radY) 
            * Matrix4x4.CreateRotationX(radX) 
            * Matrix4x4.CreateRotationZ(radZ);
    }

    private static void GetFaceVertices(Direction dir, Vector3 from, Vector3 to, Span<Vector3> corners, out Vector3 normal)
    {
        Debug.Assert(corners.Length is 4);

        // Z may need to be flipped
        switch (dir)
        {
            case Direction.Up: // +Y
                normal = Vector3.UnitY;
                corners[0] = new Vector3(from.X, to.Y, from.Z);
                corners[1] = new Vector3(from.X, to.Y, to.Z);
                corners[2] = new Vector3(to.X, to.Y, to.Z);
                corners[3] = new Vector3(to.X, to.Y, from.Z);
                break;
            case Direction.Down: // -Y
                normal = -Vector3.UnitY;
                corners[0] = new Vector3(from.X, from.Y, to.Z);
                corners[1] = new Vector3(from.X, from.Y, from.Z);
                corners[2] = new Vector3(to.X, from.Y, from.Z);
                corners[3] = new Vector3(to.X, from.Y, to.Z);
                break;
            case Direction.East: // +X
                normal = Vector3.UnitX;
                corners[0] = new Vector3(to.X, to.Y, to.Z);
                corners[1] = new Vector3(to.X, from.Y, to.Z);
                corners[2] = new Vector3(to.X, from.Y, from.Z);
                corners[3] = new Vector3(to.X, to.Y, from.Z);
                break;
            case Direction.West: // -X
                normal = -Vector3.UnitX;
                corners[0] = new Vector3(from.X, to.Y, from.Z);
                corners[1] = new Vector3(from.X, from.Y, from.Z);
                corners[2] = new Vector3(from.X, from.Y, to.Z);
                corners[3] = new Vector3(from.X, to.Y, to.Z);
                break;
            case Direction.North: // -Z
                normal = -Vector3.UnitZ;
                corners[0] = new Vector3(to.X, to.Y, from.Z);
                corners[1] = new Vector3(to.X, from.Y, from.Z);
                corners[2] = new Vector3(from.X, from.Y, from.Z);
                corners[3] = new Vector3(from.X, to.Y, from.Z);
                break;
            case Direction.South: // +Z
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

    private static int3 GetDirectionOffset(Direction dir)
        => dir switch
        {
            Direction.East => new int3(1, 0, 0),
            Direction.West => new int3(-1, 0, 0),
            Direction.Up => new int3(0, 1, 0),
            Direction.Down => new int3(0, -1, 0),
            Direction.South => new int3(0, 0, 1),
            Direction.North => new int3(0, 0, -1),
            _ => int3.Zero
        };

    private static Vector3 GetDirectionVector3(Direction dir)
        => dir switch
        {
            Direction.East => Vector3.UnitX,
            Direction.West => -Vector3.UnitX,
            Direction.Up => Vector3.UnitY,
            Direction.Down => -Vector3.UnitY,
            Direction.South => Vector3.UnitZ,
            Direction.North => -Vector3.UnitZ,
            _ => Vector3.Zero
        };

    private static Direction GetClosestDirection(Vector3 normal)
    {
        normal = Vector3.Normalize(normal);
        float maxDot = -2f; // init lower than any possible dot product (-1 to 1)
        Direction closest = Direction.Up;

        for (int i = 0; i < 6; i++)
        {
            var dir = (Direction)i;
            float dot = Vector3.Dot(normal, GetDirectionVector3(dir));
            if (dot > maxDot)
            {
                maxDot = dot;
                closest = dir;
            }
        }

        return closest;
    }

    private bool IsBlockFullAndOpaque(BlockState blockState, Direction faceDirection)
    {
        var modelVariants = ArrayPool<VariantModel>.Shared.Rent(64);

        // todo: the rng doesn't change this... right?
        var modelVariantsLength = _resourcePack.GetModelVariants(blockState, _rng, modelVariants);

        bool result = IsFaceFullAndOpaque(modelVariants.AsSpan(0, modelVariantsLength), faceDirection);

        ArrayPool<VariantModel>.Shared.Return(modelVariants);

        return result;
    }

    private bool IsFaceFullAndOpaque(ReadOnlySpan<VariantModel> modelVariants, Direction faceDirection)
    {
        Span<bool> faceGrid = stackalloc bool[16 * 16];
        faceGrid.Clear();

        Vector3 normal = GetDirectionVector3(faceDirection);

        foreach (var modelVariant in modelVariants)
        {
            var model = _resourcePack.GetBlockModel(modelVariant.Model);
            if (model is null || model.Elements.IsDefaultOrEmpty)
            {
                continue;
            }

            Matrix4x4 variantTransform = CreateVariantTransform(modelVariant);

            foreach (var element in model.Elements)
            {
                Vector3 from = element.From * BlockModelScale;
                Vector3 to = element.To * BlockModelScale;

                Matrix4x4 elementTransform = CreateElementTransform(element.Rotation);
                Matrix4x4 finalTransform = elementTransform * variantTransform;

                CalculateTransformedAABB(from, to, finalTransform, out Vector3 min, out Vector3 max);

                ProjectElementToFaceGrid(min, max, normal, faceGrid);
            }
        }

        for (int i = 0; i < 256; i++)
        {
            if (!faceGrid[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void CalculateTransformedAABB(Vector3 from, Vector3 to, Matrix4x4 transform, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);

        Span<Vector3> corners =
        [
            new Vector3(from.X, from.Y, from.Z),
            new Vector3(to.X, from.Y, from.Z),
            new Vector3(from.X, to.Y, from.Z),
            new Vector3(to.X, to.Y, from.Z),
            new Vector3(from.X, from.Y, to.Z),
            new Vector3(to.X, from.Y, to.Z),
            new Vector3(from.X, to.Y, to.Z),
            new Vector3(to.X, to.Y, to.Z)
        ];

        for (int i = 0; i < 8; i++)
        {
            var transformed = Vector3.Transform(corners[i], transform);
            min = Vector3.Min(min, transformed);
            max = Vector3.Max(max, transformed);
        }
    }

    private static void ProjectElementToFaceGrid(Vector3 min, Vector3 max, Vector3 normal, Span<bool> grid)
    {
        const float Epsilon = 0.01f;
        float uMin = 0, uMax = 0, vMin = 0, vMax = 0;
        bool touchesFace = false;

        if (normal.X < -0.5f)      // West Face
        {
            touchesFace = min.X <= Epsilon;
            uMin = min.Z; uMax = max.Z; vMin = min.Y; vMax = max.Y;
        }
        else if (normal.X > 0.5f)  // East Face
        {
            touchesFace = max.X >= 1.0f - Epsilon;
            uMin = min.Z; uMax = max.Z; vMin = min.Y; vMax = max.Y;
        }
        else if (normal.Y < -0.5f) // Down Face
        {
            touchesFace = min.Y <= Epsilon;
            uMin = min.X; uMax = max.X; vMin = min.Z; vMax = max.Z;
        }
        else if (normal.Y > 0.5f)  // Up Face
        {
            touchesFace = max.Y >= 1.0f - Epsilon;
            uMin = min.X; uMax = max.X; vMin = min.Z; vMax = max.Z;
        }
        else if (normal.Z < -0.5f) // North Face
        {
            touchesFace = min.Z <= Epsilon;
            uMin = min.X; uMax = max.X; vMin = min.Y; vMax = max.Y;
        }
        else if (normal.Z > 0.5f)  // South Face
        {
            touchesFace = max.Z >= 1.0f - Epsilon;
            uMin = min.X; uMax = max.X; vMin = min.Y; vMax = max.Y;
        }

        if (!touchesFace)
        {
            return;
        }

        // Convert normalized (0.0 - 1.0) coordinates to grid indices (0 - 16)
        int startU = Math.Clamp((int)Math.Round(uMin * 16), 0, 16);
        int endU = Math.Clamp((int)Math.Round(uMax * 16), 0, 16);
        int startV = Math.Clamp((int)Math.Round(vMin * 16), 0, 16);
        int endV = Math.Clamp((int)Math.Round(vMax * 16), 0, 16);

        for (int v = startV; v < endV; v++)
        {
            for (int u = startU; u < endU; u++)
            {
                grid[u + v * 16] = true;
            }
        }
    }
}