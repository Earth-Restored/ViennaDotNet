using ViennaDotNet.BuildplateRenderer.Models.ResourcePacks;

namespace ViennaDotNet.BuildplateRenderer;

public sealed class ResourcePackManager
{
    private readonly ResourcePack[] _packs;

    public ResourcePackManager(IReadOnlyList<(string Name, DirectoryInfo Directory)> packsToLoad)
    {
        _packs = new ResourcePack[packsToLoad.Count];

        // Load in reverse (from base to highest priority custom)
        // This allows custom packs to reference block models from base packs.
        for (int i = packsToLoad.Count - 1; i >= 0; i--)
        {
            var packDef = packsToLoad[i];

            BlockModel? FallbackResolver(string modelName)
            {
                for (int j = i + 1; j < _packs.Length; j++)
                {
                    if (_packs[j].TryGetBlockModel(modelName, out var baseModel))
                    {
                        return baseModel;
                    }
                }

                return null;
            }

            _packs[i] = ResourcePack.Load(packDef.Name, packDef.Directory, FallbackResolver);
        }
    }

    public static ResourcePackManager LoadAll(DirectoryInfo directory)
        => new ResourcePackManager(directory.EnumerateDirectories().Select(directory => (directory.Name, directory)).ToList());

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
}