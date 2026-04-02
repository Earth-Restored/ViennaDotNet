using System.Numerics;
using Serilog.Core;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SixLabors.ImageSharp;
using ViennaDotNet.Buildplate.Model;
using ViennaDotNet.BuildplateRenderer;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var resourcePack = ResourcePack.Load(new DirectoryInfo("~/Downloads/minecraft".Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));

        WorldData? worldData;

        using (var fs = File.OpenRead("test.zip"))
        {
            worldData = await WorldData.LoadFromZipAsync(fs, Logger.None);
        }

        if (worldData is null)
        {
            Console.WriteLine("Failed to load world data.");
            return;
        }

        var meshGenerator = new MeshGenerator(resourcePack);

        var mesh = await meshGenerator.GenerateAsync(worldData);

        await ExportToGlb(mesh, "/home/bitcoder/Downloads/_abc.glb", name =>
        {
            return resourcePack.GetTextureDataPNGAsync(name);
        });

        Console.WriteLine("Done");
    }

    public static async Task ExportToGlb(MeshData meshData, string outputPath, Func<string, Task<byte[]>> textureFetcher)
    {
        var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>("ExportedMesh");

        foreach (var kvp in meshData.Primitives)
        {
            string textureId = kvp.Key;
            MeshPrimitive primitiveData = kvp.Value;

            byte[] textureBytes = await textureFetcher(textureId);

            var material = new MaterialBuilder(textureId)
                .WithBaseColor(new SharpGLTF.Memory.MemoryImage(textureBytes))
                .WithDoubleSide(false)
                .WithAlpha(AlphaMode.OPAQUE); // todo: BLEND

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
        model.SaveGLB(outputPath);
    }

    private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> CreateVertexBuilder(MeshVertex v)
    {
        var geometry = new VertexPositionNormal(v.Position, v.Normal);

        // Note: glTF expects UV origin (0,0) at the Top-Left of the texture. 
        // If your source data expects Bottom-Left, you may need to invert the V axis:
        // var material = new VertexTexture1(new Vector2(v.UV.X, 1.0f - v.UV.Y));
        var material = new VertexTexture1(v.UV);

        return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(geometry, material);
    }
}