using SharpGLTF.Schema2;
using SixLabors.ImageSharp.PixelFormats;
using ViennaDotNet.BuildplateRenderer.Models.ResourcePacks;

namespace ViennaDotNet.BuildplateRenderer;

public sealed class ResourcePackManager
{
    private readonly ResourcePack[] _packs;

    private readonly Dictionary<string, SixLabors.ImageSharp.Image<Rgba32>> _textureCache = [];

    private ResourcePackManager(ResourcePack[] packs)
    {
        _packs = packs;
    }

    public int LoadedPackCount => _packs.Length;

    public static async Task<ResourcePackManager> LoadAllAsync(DirectoryInfo directory, CancellationToken cancellationToken = default)
        => await LoadAsync(directory.EnumerateDirectories().Select(directory => (directory.Name, directory)).ToList(), cancellationToken);

    public static async Task<ResourcePackManager> LoadAsync(IReadOnlyList<(string Name, DirectoryInfo Directory)> packsToLoad, CancellationToken cancellationToken = default)
    {
        var packs = new ResourcePack[packsToLoad.Count];

        // Load in reverse (from base to highest priority custom)
        // This allows custom packs to reference block models from base packs.
        for (int i = packsToLoad.Count - 1; i >= 0; i--)
        {
            var packDef = packsToLoad[i];

            BlockModel? FallbackResolver(string modelName)
            {
                for (int j = i + 1; j < packs.Length; j++)
                {
                    if (packs[j].TryGetBlockModel(modelName, out var baseModel))
                    {
                        return baseModel;
                    }
                }

                return null;
            }

            packs[i] = await ResourcePack.LoadAsync(packDef.Name, packDef.Directory, FallbackResolver, cancellationToken);
        }

        return new ResourcePackManager(packs);
    }

    public int GetModelVariants(BlockState blockState, Random rng, Span<VariantModel> result)
    {
        for (int i = 0; i < _packs.Length; i++)
        {
            int count = _packs[i].GetModelVariants(blockState, rng, result);
            if (count > 0)
            {
                return count;
            }
        }

        throw new KeyNotFoundException($"BlockState variant for '{blockState.BlockId}' not found in any loaded resource pack.");
    }

    public BlockModel GetBlockModel(string modelName)
    {
        for (int i = 0; i < _packs.Length; i++)
        {
            if (_packs[i].TryGetBlockModel(modelName, out var model))
            {
                return model;
            }
        }

        throw new KeyNotFoundException($"BlockModel '{modelName}' not found in any loaded resource pack.");
    }

    public async Task<byte[]> GetTextureDataPNGAsync(string name, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < _packs.Length; i++)
        {
            var textureData = await _packs[i].TryGetTextureDataPNGAsync(name, cancellationToken);
            if (textureData is not null)
            {
                return textureData;
            }
        }

        throw new FileNotFoundException($"Texture '{name}' not found in any loaded resource pack.");
    }

    public async Task<SixLabors.ImageSharp.Image<Rgba32>> GetTextureImageAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_textureCache.TryGetValue(name, out var image))
        {
            return image;
        }

        for (int i = 0; i < _packs.Length; i++)
        {
            var textureData = await _packs[i].TryGetTextureDataPNGAsync(name, cancellationToken);
            if (textureData is not null)
            {
                using (var ms = new MemoryStream(textureData))
                {
                    image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(ms, cancellationToken);
                    _textureCache.Add(name, image);
                    return image;
                }
            }
        }

        throw new FileNotFoundException($"Colormap texture '{name}' not found in any loaded resource pack.");
    }
}