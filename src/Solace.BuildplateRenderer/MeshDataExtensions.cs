using System.Collections.Frozen;
using System.Numerics;
using Serilog;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ViennaDotNet.BuildplateRenderer;

public static class MeshDataExtensions
{
    private static readonly FrozenDictionary<string, string> TextureToColormap = new Dictionary<string, string>
        {
            { "minecraft:block/grass_block_top", "minecraft:colormap/grass.png" },
            { "minecraft:block/grass_block_side_overlay", "minecraft:colormap/grass.png" },
            { "minecraft:block/fern", "minecraft:colormap/grass.png" },
            { "minecraft:block/large_fern_bottom", "minecraft:colormap/grass.png" },
            { "minecraft:block/large_fern_top", "minecraft:colormap/grass.png" },
            { "minecraft:block/tall_grass_bottom", "minecraft:colormap/grass.png" },
            { "minecraft:block/tall_grass_top", "minecraft:colormap/grass.png" },
            { "minecraft:block/short_grass", "minecraft:colormap/grass.png" },
            { "minecraft:block/oak_leaves", "minecraft:colormap/foliage.png" },
            { "minecraft:block/jungle_leaves", "minecraft:colormap/foliage.png" },
            { "minecraft:block/acacia_leaves", "minecraft:colormap/foliage.png" },
            { "minecraft:block/dark_oak_leaves", "minecraft:colormap/foliage.png" },
            { "minecraft:block/mangrove_leaves", "minecraft:colormap/foliage.png" },
            { "minecraft:block/vine", "minecraft:colormap/foliage.png" }
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, Vector4> HardcodedBlockColors = new Dictionary<string, Vector4>
        {
            { "minecraft:block/spruce_leaves", HexToVector4(0x619961) },
            { "minecraft:block/birch_leaves", HexToVector4(0x80A755) },
            { "minecraft:block/lily_pad", HexToVector4(0x208030) },
            { "minecraft:block/pumpkin_stem", HexToVector4(0xEFC00F) },
            { "minecraft:block/attached_pumpkin_stem", HexToVector4(0xEFC00F) },
            { "minecraft:block/melon_stem", HexToVector4(0xFFFF00) },
            { "minecraft:block/attached_melon_stem", HexToVector4(0xFFFF00) },
        }.ToFrozenDictionary();

    private static Vector4 HexToVector4(int hex)
        => new Vector4(
            ((hex >> 16) & 0xFF) / 255.0f,
            ((hex >> 8) & 0xFF) / 255.0f,
            (hex & 0xFF) / 255.0f,
            1.0f
        );

    private static async Task<Vector4?> TryGetColorMultiplierAsync(string textureName, Biome biome, ResourcePackManager resourcePackManager)
    {
        if (!textureName.AsSpan().Contains(':'))
        {
            textureName = "minecraft:" + textureName;
        }

        if (HardcodedBlockColors.TryGetValue(textureName, out var color))
        {
            return color;
        }

        if (!TextureToColormap.TryGetValue(textureName, out var colormapPath))
        {
            return null;
        }

        if (TryGetBiomeOverride(textureName, biome, out color))
        {
            return color;
        }

        return await GetColorFromTexture(colormapPath, biome, resourcePackManager);
    }

    private static bool TryGetBiomeOverride(string blockId, Biome biome, out Vector4 overrideColor)
    {
        if (biome.Name is "swamp")
        {
            overrideColor = HexToVector4(0x6A7039);
            return true;
        }

        if (biome.Name.Contains("badlands") && (blockId.Contains("grass") || blockId.Contains("fern")))
        {
            overrideColor = HexToVector4(0x90814D);
            return true;
        }

        overrideColor = Vector4.Zero;
        return false;
    }

    private static async Task<Vector4?> GetColorFromTexture(string path, Biome biome, ResourcePackManager resourcePackManager)
    {
        float temp = Math.Clamp(biome.Temperature, 0f, 1f);
        float humidity = Math.Clamp(biome.Downfall, 0f, 1f) * temp;

        int u = (int)((1.0f - temp) * 255.0f);
        int v = (int)((1.0f - humidity) * 255.0f);

        try
        {
            var img = await resourcePackManager.GetTextureImageAsync(path);
            Rgba32 pixel = img[u, v];
            return new Vector4(pixel.R / 255f, pixel.G / 255f, pixel.B / 255f, 1.0f);
        }
        catch
        {
            return null;
        }
    }

    private sealed record Biome(string Name, float Temperature, float Downfall);

    extension(MeshData mesh)
    {
        public async Task ToGlbAsync(ResourcePackManager resourcePack, Stream outputStream, SharpGLTF.Schema2.WriteSettings? settings = null)
        {
            var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>("ExportedMesh");

            var biome = new Biome("forest", 0.7f, 0.8f);

            foreach (var kvp in mesh.Primitives)
            {
                string textureId = kvp.Key;
                MeshPrimitive primitiveData = kvp.Value;

                byte[] textureBytes = await resourcePack.GetTextureDataPNGAsync(textureId);

                Vector4? colorMultiplier = await TryGetColorMultiplierAsync(textureId, biome, resourcePack);

                var material = new MaterialBuilder(textureId)
                    .WithBaseColor(new SharpGLTF.Memory.MemoryImage(textureBytes), colorMultiplier)
                    .WithDoubleSide(false)
                    .WithAlpha(AlphaMode.MASK) // todo: BLEND
                    .WithMetallicRoughness(0, 1);

                var textureBuilder = material.GetChannel(KnownChannel.BaseColor).Texture;
                textureBuilder.MinFilter = SharpGLTF.Schema2.TextureMipMapFilter.NEAREST;
                textureBuilder.MagFilter = SharpGLTF.Schema2.TextureInterpolationFilter.NEAREST;

                var gltfPrimitive = meshBuilder.UsePrimitive(material);

                var verts = primitiveData.Vertices;
                var indices = primitiveData.Indices;

                for (int i = 0; i < indices.Count; i += 3)
                {
                    var v1 = CreateVertexBuilder(verts[indices[i]]);
                    var v2 = CreateVertexBuilder(verts[indices[i + 1]]);
                    var v3 = CreateVertexBuilder(verts[indices[i + 2]]);

                    gltfPrimitive.AddTriangle(v1, v2, v3);
                }
            }

            var sceneBuilder = new SceneBuilder();
            sceneBuilder.AddRigidMesh(meshBuilder, Matrix4x4.Identity);

            var model = sceneBuilder.ToGltf2();
            model.WriteGLB(outputStream, settings);
        }

        private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> CreateVertexBuilder(MeshVertex v)
        {
            var geometry = new VertexPositionNormal(v.Position, v.Normal);

            var material = new VertexTexture1(v.UV);

            return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(geometry, material);
        }
    }
}