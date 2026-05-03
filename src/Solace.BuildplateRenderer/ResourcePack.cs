using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using BitcoderCZ.Buffers;
using Solace.BuildplateRenderer.Models.ResourcePacks;
using Solace.BuildplateRenderer.Utils;
using BSVBufferArray = BitcoderCZ.Buffers.FixedArray1<Solace.BuildplateRenderer.Models.ResourcePacks.VariantModel>;
using BSVBuffer = BitcoderCZ.Buffers.ImmutableInlineArray<BitcoderCZ.Buffers.FixedArray1<Solace.BuildplateRenderer.Models.ResourcePacks.VariantModel>, Solace.BuildplateRenderer.Models.ResourcePacks.VariantModel>;
using MPSBufferArray = BitcoderCZ.Buffers.FixedArray1<string>;
using MPSBuffer = BitcoderCZ.Buffers.ImmutableInlineArray<BitcoderCZ.Buffers.FixedArray1<string>, string>;
using System.Runtime.InteropServices;
using BitcoderCZ.Utils;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Solace.BuildplateRenderer;

// https://minecraft.wiki/w/Resource_pack
// https://minecraft.wiki/w/Model
public sealed class ResourcePack
{
    private readonly string _namePrefix;
    private readonly DirectoryInfo _rootDir;
    private readonly DirectoryInfo _texturesDir;

    private readonly FrozenDictionary<string, BlockModel> _blockModels;
    private readonly FrozenDictionary<string, HashSet<string>> _variantPropertySchema;
    private readonly FrozenDictionary<BlockState, (BSVBuffer Buffer, int TotalWeight)> _blockStatesVariant;
    private readonly FrozenDictionary<string, ImmutableArray<MultipartCase>> _blockStatesMultipart;

    private readonly Dictionary<string, byte[]> _textures = [];

    public ResourcePack(string name, DirectoryInfo rootDir, FrozenDictionary<string, BlockModel> blockModels, FrozenDictionary<string, HashSet<string>> variantPropertySchema, FrozenDictionary<BlockState, (BSVBuffer Buffer, int TotalWeight)> blockStatesVariant, FrozenDictionary<string, ImmutableArray<MultipartCase>> blockStatesMultipart)
    {
        Name = name;
        _namePrefix = $"{Name}:";
        _blockModels = blockModels;
        _variantPropertySchema = variantPropertySchema;
        _blockStatesVariant = blockStatesVariant;
        _blockStatesMultipart = blockStatesMultipart;
        _rootDir = rootDir;
        _texturesDir = new DirectoryInfo(Path.Combine(_rootDir.FullName, "textures"));
    }

    public string Name { get; }

    public static async Task<ResourcePack> LoadAsync(string packName, DirectoryInfo rootDir, Func<string, BlockModel?>? fallbackResolver = null, CancellationToken cancellationToken = default)
    {
        var blockModelsDir = new DirectoryInfo(Path.Combine(rootDir.FullName, "models", "block"));
        var blockModelsJson = new Dictionary<string, BlockModelJson>();

        if (blockModelsDir.Exists)
        {
            foreach (var file in blockModelsDir.EnumerateFiles())
            {
                string modelName = Path.GetFileNameWithoutExtension(file.Name);
                BlockModelJson model;
                using (var fs = File.OpenRead(file.FullName))
                {
                    model = await JsonUtils.DeserializeJsonAsync<BlockModelJson>(fs, cancellationToken) ?? new();
                }

                blockModelsJson.Add($"{packName}:block/{modelName}", model);
            }
        }

        var blockModels = new Dictionary<string, BlockModel>(blockModelsJson.Count);
        foreach (var (modelName, _) in blockModelsJson)
        {
            ResolveBlockModel(modelName);
        }

        var blockStatesDir = new DirectoryInfo(Path.Combine(rootDir.FullName, "blockstates"));
        Dictionary<BlockState, (BSVBuffer Buffer, int TotalWeight)> blockStatesVariant = [];
        Dictionary<string, ImmutableArray<MultipartCase>> blockStatesMultipart = [];
        if (blockStatesDir.Exists)
        {
            foreach (var file in blockStatesDir.EnumerateFiles())
            {
                string blockName = $"{packName}:{Path.GetFileNameWithoutExtension(file.Name)}";
                BlockStateJson json;
                using (var fs = File.OpenRead(file.FullName))
                {
                    json = await JsonUtils.DeserializeJsonAsync<BlockStateJson>(fs, cancellationToken) ?? new();
                }

                if (json.Variants is not null)
                {
                    foreach (var variant in json.Variants)
                    {
                        var props = ParseVariantString(variant.Key);
                        var state = BlockState.CreateNoCopy(blockName, props);

                        int totalWeight = 0;
                        foreach (var item in variant.Value)
                        {
                            totalWeight += item.Weight;
                        }

                        blockStatesVariant[state] = (ImmutableInlineArray.Create<BSVBufferArray, VariantModel>(variant.Value), totalWeight);
                    }
                }
                else if (json.Multipart is not null)
                {
                    var builder = ImmutableArray.CreateBuilder<MultipartCase>(json.Multipart.Length);
                    foreach (var @case in json.Multipart)
                    {
                        int totalWeight = 0;
                        foreach (var item in @case.Apply)
                        {
                            totalWeight += item.Weight;
                        }

                        // Or<And<State>>
                        //public ImmutableArray<ImmutableArray<KeyValuePair<string, MPSBuffer>>> Conditions { get; init; }
                        ImmutableArray<ImmutableArray<KeyValuePair<string, MPSBuffer>>> conditions = default;
                        if (@case.When is { } when)
                        {
                            if (when.And is not null)
                            {
                                var conditionsBuilder = ImmutableArray.CreateBuilder<KeyValuePair<string, MPSBuffer>>(when.And.Count);

                                foreach (var list in when.And)
                                {
                                    foreach (var item in list)
                                    {
                                        conditionsBuilder.Add(new(item.Key, CreateMultiPartState(item.Value)));
                                    }
                                }

                                conditions = [conditionsBuilder.DrainToImmutable()];
                            }
                            else if (when.Or is not null)
                            {
                                var conditionsBuilder = ImmutableArray.CreateBuilder<ImmutableArray<KeyValuePair<string, MPSBuffer>>>(when.Or.Count);
                                var innerConditionsBuilder = ImmutableArray.CreateBuilder<KeyValuePair<string, MPSBuffer>>(4);

                                foreach (var list in when.Or)
                                {
                                    foreach (var item in list)
                                    {
                                        innerConditionsBuilder.Add(new(item.Key, CreateMultiPartState(item.Value)));
                                    }

                                    conditionsBuilder.Add(innerConditionsBuilder.DrainToImmutable());
                                }

                                conditions = conditionsBuilder.DrainToImmutable();
                            }
                            else if (when.Properties is not null)
                            {
                                var conditionsBuilder = ImmutableArray.CreateBuilder<KeyValuePair<string, MPSBuffer>>(when.Properties.Count);

                                foreach (var item in when.Properties)
                                {
                                    conditionsBuilder.Add(new(item.Key, CreateMultiPartState(item.Value.GetString() ?? "")));
                                }

                                conditions = [conditionsBuilder.DrainToImmutable()];
                            }
                        }

                        builder.Add(new MultipartCase()
                        {
                            When = new MultipartCaseCondition()
                            {
                                Conditions = conditions,
                            },
                            Apply = @case.Apply,
                            TotalWeight = totalWeight,
                        });
                    }

                    blockStatesMultipart[blockName] = builder.DrainToImmutable();
                }
            }
        }

        Dictionary<string, HashSet<string>> variantPropertySchema = new(blockStatesVariant.Count);
        foreach (var item in blockStatesVariant)
        {
            if (variantPropertySchema.ContainsKey(item.Key.BlockId))
            {
                continue;
            }

            var propertyNames = new HashSet<string>(item.Key.PropertyCount);
            foreach (var prop in item.Key.Properties)
            {
                propertyNames.Add(prop.Key);
            }

            variantPropertySchema.Add(item.Key.BlockId, propertyNames);
        }

        return new ResourcePack(packName, rootDir, blockModels.ToFrozenDictionary(), variantPropertySchema.ToFrozenDictionary(), blockStatesVariant.ToFrozenDictionary(), blockStatesMultipart.ToFrozenDictionary());

        BlockModel? ResolveBlockModel(string modelName)
        {
            if (!modelName.Contains(':'))
            {
                modelName = $"{packName}:{modelName}";
            }

            if (blockModels.TryGetValue(modelName, out var existingModel))
            {
                return existingModel;
            }

            if (!blockModelsJson.TryGetValue(modelName, out var json))
            {
                if (fallbackResolver is not null)
                {
                    return fallbackResolver(modelName);
                }

                return null;
            }

            var parent = json.Parent is null ? null : ResolveBlockModel(json.Parent);

            var textures = MergeDictionaries(json.Textures, parent?.Textures);

            ImmutableArray<BlockElement> elements;
            if (json.Elements is null)
            {
                if (parent?.Elements is null)
                {
                    elements = [];
                }
                else
                {
                    elements = parent.Elements;
                }
            }
            else
            {
                var elementBuilder = ImmutableArray.CreateBuilder<BlockElement>(json.Elements.Length);

                foreach (var element in json.Elements)
                {
                    BlockElementRotation? rotation = null;
                    if (element.Rotation is { } eRot)
                    {
                        if (eRot.Axis is { } axis && eRot.Angle is { } angle)
                        {
                            rotation = new BlockElementRotation()
                            {
                                Origin = eRot.Origin,
                                ReScale = eRot.ReScale,
                                X = axis is Axis.X ? angle : 0,
                                Y = axis is Axis.Y ? angle : 0,
                                Z = axis is Axis.Z ? angle : 0,
                            };
                        }
                        else
                        {
                            rotation = new BlockElementRotation()
                            {
                                Origin = eRot.Origin,
                                ReScale = eRot.ReScale,
                                X = eRot.X,
                                Y = eRot.Y,
                                Z = eRot.Z,
                            };
                        }
                    }

                    var faces = new BlockElementFaces();
                    faces[0] = CreateBlockFace(element.Faces.East, element.From, element.To, 0);
                    faces[1] = CreateBlockFace(element.Faces.West, element.From, element.To, 1);
                    faces[2] = CreateBlockFace(element.Faces.Up, element.From, element.To, 2);
                    faces[3] = CreateBlockFace(element.Faces.Down, element.From, element.To, 3);
                    faces[4] = CreateBlockFace(element.Faces.South, element.From, element.To, 4);
                    faces[5] = CreateBlockFace(element.Faces.North, element.From, element.To, 5);

                    elementBuilder.Add(new BlockElement()
                    {
                        From = element.From,
                        To = element.To,
                        Rotation = rotation,
                        Shade = element.Shade,
                        LightEmission = element.LightEmission,
                        Faces = faces,
                    });
                }

                elements = elementBuilder.DrainToImmutable();
            }

            var model = new BlockModel()
            {
                Display = MergeDictionaries(json.Display, parent?.Display),
                Textures = textures,
                Elements = elements,
            };

            blockModels[modelName] = model;

            return model;
        }

        static BlockFace? CreateBlockFace(BlockFaceJson? json, Vector3 from, Vector3 to, int faceIndex)
        {
            if (json is null)
            {
                return null;
            }

            if (json.UV is not { } uv)
            {
                const float MaxValue = 16f;

                uv = faceIndex switch
                {
                    0 => new UVCoordinates(from.Z, MaxValue - to.Y, to.Z, MaxValue - from.Y),
                    1 => new UVCoordinates(MaxValue - to.Z, MaxValue - to.Y, MaxValue - from.Z, MaxValue - from.Y),
                    2 => new UVCoordinates(from.X, from.Z, to.X, to.Z),
                    3 => new UVCoordinates(from.X, MaxValue - to.Z, to.X, MaxValue - from.Z),
                    4 => new UVCoordinates(from.X, MaxValue - to.Y, to.X, MaxValue - from.Y),
                    5 => new UVCoordinates(MaxValue - to.X, MaxValue - to.Y, MaxValue - from.X, MaxValue - from.Y),
                    _ => new UVCoordinates(0, 0, MaxValue, MaxValue)
                };
            }

            Debug.Assert(json.Texture.StartsWith('#'));

            return new BlockFace()
            {
                UV = uv,
                Texture = json.Texture,
                CullFace = json.CullFace switch
                {
                    null => null,
                    DirectionJson.East => Direction.East,
                    DirectionJson.West => Direction.West,
                    DirectionJson.Up or DirectionJson.Top => Direction.Up,
                    DirectionJson.Down or DirectionJson.Bottom => Direction.Down,
                    DirectionJson.South => Direction.South,
                    DirectionJson.North => Direction.North,
                    _ => throw new UnreachableException(),
                },
                Rotation = json.Rotation,
                TintIndex = json.TintIndex,
            };
        }

        static KeyValuePair<string, string>[] ParseVariantString(string variantStr)
        {
            if (string.IsNullOrWhiteSpace(variantStr))
            {
                return [];
            }

            return [.. variantStr.Split(',')
                .Select(part => part.Split('='))
                .Where(parts => parts.Length == 2)
                .Select(parts => new KeyValuePair<string, string>(parts[0], parts[1]))];
        }

        static MPSBuffer CreateMultiPartState(string value)
        {
            var span = value.AsSpan();
            if (!span.Contains('|'))
            {
                return ImmutableInlineArray.Create<MPSBufferArray, string>(value);
            }

            var result = new MPSBuffer.Builder();

            foreach (var range in span.Split('|'))
            {
                result.Add(value[range]);
            }

            return result.DrainToImmutable(true);
        }
    }

    /// <summary>
    /// Gets the model variants to render for a given <see cref="BlockState"/>.
    /// </summary>
    /// <param name="blockState">The <see cref="BlockState"/>.</param>
    /// <param name="rng">The RNG deciding which model to choose.</param>
    /// <param name="result">The result model variants.</param>
    /// <returns>The number of model variants.</returns>
    public int GetModelVariants(BlockState blockState, Random rng, Span<VariantModel> result)
    {
        ThrowHelper.ThrowIfLessThan(result.Length, 1);

        if (!blockState.BlockId.StartsWith(_namePrefix))
        {
            return 0;
        }

        // way more variant blocks, so try variant first
        if (_variantPropertySchema.TryGetValue(blockState.BlockId, out var propertySchema))
        {
            if (blockState.PropertyCount == propertySchema.Count && _blockStatesVariant.TryGetValue(blockState, out var variant))
            {
                var (variants, totalWeight) = variant;

                result[0] = PickRandomVariant(variants, totalWeight, rng);
                return 1;
            }

            if (propertySchema.Count is 0 && _blockStatesVariant.TryGetValue(new BlockState(blockState.BlockId), out variant))
            {
                var (variants, totalWeight) = variant;

                result[0] = PickRandomVariant(variants, totalWeight, rng);
                return 1;
            }

            var propertiesArray = ArrayPool<KeyValuePair<string, string>>.Shared.Rent(propertySchema.Count);
            int propertiesArrayLength = 0;
            foreach (var item in blockState.Properties)
            {
                if (propertySchema.Contains(item.Key))
                {
                    propertiesArray[propertiesArrayLength++] = item;
                }
            }

            if (_blockStatesVariant.TryGetValue(BlockState.CreateNoCopy(blockState.BlockId, propertiesArray, propertiesArrayLength), out variant))
            {
                var (variants, totalWeight) = variant;

                result[0] = PickRandomVariant(variants, totalWeight, rng);
                return 1;
            }

            ArrayPool<KeyValuePair<string, string>>.Shared.Return(propertiesArray);
        }

        if (!_blockStatesMultipart.TryGetValue(blockState.BlockId, out var multipart))
        {
            return 0;
        }

        int resultLength = 0;
        foreach (var item in multipart)
        {
            if (item.When is null || DoesConditionMatch(item.When.Value, blockState))
            {
                result[resultLength++] = PickRandomVariant(item.Apply, item.TotalWeight, rng);
            }
        }

        Debug.Assert(resultLength > 0);
        return resultLength;

        static VariantModel PickRandomVariant<TCollection>(TCollection variants, int totalWeight, Random rng)
            where TCollection : IReadOnlyList<VariantModel>
        {
            if (variants.Count is 1)
            {
                return variants[0];
            }

            float r = rng.NextSingle() * totalWeight;

            float cumulative = 0f;
            foreach (var variant in variants)
            {
                cumulative += variant.Weight;
                if (r < cumulative)
                {
                    return variant;
                }
            }

            return variants[^1];
        }

        static bool DoesConditionMatch(MultipartCaseCondition condition, BlockState blockState)
        {
            if (condition.Conditions.IsDefaultOrEmpty)
            {
                return true;
            }

            foreach (var andGroup in condition.Conditions.AsSpan())
            {
                if (AndGroupMatches(andGroup, blockState))
                {
                    return true;
                }
            }

            return false;
        }

        static bool AndGroupMatches(ImmutableArray<KeyValuePair<string, MPSBuffer>> andGroup, BlockState blockState)
        {
            foreach (var requirement in andGroup.AsSpan())
            {
                string targetProperty = requirement.Key;
                MPSBuffer allowedValues = requirement.Value;

                if (!StateSatisfiesRequirement(blockState, targetProperty, allowedValues))
                {
                    return false;
                }
            }

            return true;
        }

        static bool StateSatisfiesRequirement(BlockState blockState, string key, MPSBuffer allowedValues)
        {
            foreach (var property in blockState.Properties)
            {
                if (property.Key == key)
                {
                    for (int i = 0; i < allowedValues.Count; i++)
                    {
                        if (allowedValues[i] == property.Value)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            return false;
        }
    }

    public BlockModel GetBlockModel(string modelName)
        => _blockModels[modelName];

    public bool TryGetBlockModel(string modelName, [NotNullWhen(true)] out BlockModel? model)
        => _blockModels.TryGetValue(modelName, out model);

    public async Task<byte[]> GetTextureDataPNGAsync(string name, CancellationToken cancellationToken = default)
    {
        var textureData = await TryGetTextureDataPNGAsync(name, cancellationToken);

        return textureData is null ? throw new FileNotFoundException() : textureData;
    }

    public async Task<byte[]?> TryGetTextureDataPNGAsync(string name, CancellationToken cancellationToken = default)
    {
        if (name.StartsWith(_namePrefix))
        {
            name = name[_namePrefix.Length..];
        }

        var file = Path.Combine(_texturesDir.FullName, name);
        if (!Path.HasExtension(file))
        {
            file += ".png";
        }

        if (!File.Exists(file))
        {
            return null;
        }

        var infoFile = file + ".mcmeta";
        if (!File.Exists(infoFile))
        {
            try
            {
                return await File.ReadAllBytesAsync(file, cancellationToken);
            }
            catch
            {
                return null;
            }
        }

        // we only support non animated textures, so crop to the first frame
        TextureInfoJson? textureInfoJson;
        using (var fs = File.OpenRead(infoFile))
        {
            textureInfoJson = await JsonUtils.DeserializeJsonAsync<TextureInfoJson>(fs, cancellationToken) ?? new TextureInfoJson() { Animation = new TextureAnimationJson(), };
        }

        Image textureImage;
        using (var fs = File.OpenRead(file))
        {
            textureImage = await Image.LoadAsync(fs, cancellationToken);
        }

        if (textureInfoJson.Animation.Width is not { } width)
        {
            if (textureInfoJson.Animation.Height is not null)
            {
                width = textureImage.Width;
            }
            else
            {
                width = int.Min(textureImage.Width, textureImage.Height);
            }
        }

        if (textureInfoJson.Animation.Height is not { } height)
        {
            if (textureInfoJson.Animation.Width is not null)
            {
                height = textureImage.Height;
            }
            else
            {
                height = int.Min(textureImage.Width, textureImage.Height);
            }
        }

        int frameCount = textureImage.Height / height;

        var textureInfo = new TextureInfo()
        {
            Animation = new TextureAnimation()
            {
                Interpolate = textureInfoJson.Animation.Interpolate,
                Width = width,
                Height = height,
                FrameTime = textureInfoJson.Animation.FrameTime,
                Frames = textureInfoJson.Animation.Frames is { } frames ? ImmutableCollectionsMarshal.AsImmutableArray(frames) : [.. Enumerable.Range(0, frameCount)],
            },
        };

        var firstFrameIndex = textureInfo.Animation.Frames.IsDefaultOrEmpty ? 0 : textureInfo.Animation.Frames[0];

        textureImage.Mutate(ctx =>
        {
            ctx.Crop(new Rectangle(0, firstFrameIndex * height, width, height));
        });

        using (var ms = new MemoryStream())
        {
            await textureImage.SaveAsPngAsync(ms, cancellationToken);
            textureImage.Dispose();

            return ms.ToArray();
        }
    }

    private static IReadOnlyDictionary<TKey, TValue> MergeDictionaries<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? @new, IReadOnlyDictionary<TKey, TValue>? @base)
        where TKey : notnull
    {
        if (@base is null or { Count: 0 })
        {
            return @new ?? new Dictionary<TKey, TValue>();
        }

        if (@new is null or { Count: 0 })
        {
            return @base ?? new Dictionary<TKey, TValue>();
        }

        var result = new Dictionary<TKey, TValue>(@new.Count + @base.Count);

        foreach (var (key, item) in @base)
        {
            result.Add(key, item);
        }

        foreach (var (key, item) in @new)
        {
            result[key] = item; // override base
        }

        return result;
    }
}