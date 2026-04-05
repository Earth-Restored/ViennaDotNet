using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace ViennaDotNet.BuildplateRenderer;

public static class MeshDataExtensions
{
    extension(MeshData mesh)
    {
        public async Task ToGlb(ResourcePackManager resourcePack, Stream outputStream, SharpGLTF.Schema2.WriteSettings? settings = null)
        {
            var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>("ExportedMesh");

            foreach (var kvp in mesh.Primitives)
            {
                string textureId = kvp.Key;
                MeshPrimitive primitiveData = kvp.Value;

                byte[] textureBytes = await resourcePack.GetTextureDataPNGAsync(textureId);

                var material = new MaterialBuilder(textureId)
                    .WithBaseColor(new SharpGLTF.Memory.MemoryImage(textureBytes))
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