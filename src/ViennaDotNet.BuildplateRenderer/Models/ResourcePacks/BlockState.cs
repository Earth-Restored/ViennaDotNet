using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViennaDotNet.BuildplateRenderer.JsonConverters;
using MPSBufferArray = BitcoderCZ.Buffers.FixedArray1<string>;
using MPSBuffer = BitcoderCZ.Buffers.ImmutableInlineArray<BitcoderCZ.Buffers.FixedArray1<string>, string>;

namespace ViennaDotNet.BuildplateRenderer.Models.ResourcePacks;

[StructLayout(LayoutKind.Auto)]
public readonly struct BlockState : IEquatable<BlockState>
{
    public readonly string BlockId;
    public readonly KeyValuePair<string, string>[] Properties;

    private readonly int _hashCode;

    public BlockState(string blockId, ReadOnlySpan<KeyValuePair<string, string>> properties)
    {
        BlockId = blockId;

        Properties = new KeyValuePair<string, string>[properties.Length];
        properties.CopyTo(Properties);
        Properties.AsSpan().Sort((a, b) => a.Key.CompareTo(b.Key));

        var hash = new HashCode();
        hash.Add(BlockId);
        foreach (var prop in Properties)
        {
            hash.Add(prop.Key);
            hash.Add(prop.Value);
        }

        _hashCode = hash.ToHashCode();
    }

    public BlockState(string blockId, IEnumerable<KeyValuePair<string, string>> properties)
    {
        BlockId = blockId;

        Properties = [.. properties.OrderBy(p => p.Key)];

        var hash = new HashCode();
        hash.Add(BlockId);
        foreach (var prop in Properties)
        {
            hash.Add(prop.Key);
            hash.Add(prop.Value);
        }

        _hashCode = hash.ToHashCode();
    }

    public static bool operator ==(BlockState left, BlockState right)
        => left.Equals(right);

    public static bool operator !=(BlockState left, BlockState right)
        => !(left == right);

    public bool Equals(BlockState other)
    {
        if (_hashCode != other._hashCode ||
            BlockId != other.BlockId ||
            Properties.Length != other.Properties.Length)
        {
            return false;
        }

        for (int i = 0; i < Properties.Length; i++)
        {
            if (Properties[i].Key != other.Properties[i].Key ||
                Properties[i].Value != other.Properties[i].Value)
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
        => obj is BlockState state && Equals(state);

    public override int GetHashCode()
        => _hashCode;
}

// https://minecraft.wiki/w/Blockstates_definition#JSON_format
public sealed class BlockStateJson
{
    // mutually exclusive with with Variants
    public MultipartCaseJson[]? Multipart { get; init; }

    // mutually exclusive with with Multipart
    // if there is only 1 variant, key is ""
    [JsonPropertyName("variants")]
    public Dictionary<string, VariantModel[]>? Variants { get; init; }
}

public sealed class MultipartCaseJson
{
    // if null, always applies
    public MultipartCaseConditionJson? When { get; init; }

    [JsonConverter(typeof(SingleOrArrayConverter<VariantModel>))]
    public required List<VariantModel> Apply { get; init; }
}

public sealed class MultipartCase
{
    // if null, always applies
    public MultipartCaseCondition? When { get; init; }

    public required List<VariantModel> Apply { get; init; }

    public int TotalWeight {get; init;}
}

[StructLayout(LayoutKind.Auto)]
public readonly struct MultipartCaseConditionJson
{
    // mutually exclusive with And, Properties
    [JsonPropertyName("OR")]
    public List<Dictionary<string, string>>? Or { get; init; }

    // mutually exclusive with Or, Properties
    [JsonPropertyName("AND")]
    public List<Dictionary<string, string>>? And { get; init; }

    // mutually exclusive with And, Or
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Properties { get; init; }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct MultipartCaseCondition
{
    // Or<And<State>>
    public ImmutableArray<ImmutableArray<KeyValuePair<string, MPSBuffer>>> Conditions { get; init; }
}

public sealed class VariantModel : IEquatable<VariantModel>
{
    public required string Model { get; init; }

    [JsonPropertyName("x")]
    public int RotationX { get; init; } // in degrees

    [JsonPropertyName("y")]
    public int RotationY { get; init; } // in degrees

    [JsonPropertyName("z")]
    public int RotationZ { get; init; } // in degrees

    // locks the rotation of the texture of a block, if set to true. This way the texture does not rotate with the block when the x and y rotation.
    [JsonPropertyName("uvlock")]
    public bool UVLock { get; init; }

    [JsonPropertyName("weight")]
    public int Weight { get; init; } = 1;

    public bool Equals(VariantModel? other)
        => other is not null && Model == other.Model && RotationX == other.RotationX && RotationY == other.RotationY && RotationZ == other.RotationZ && UVLock == other.UVLock && Weight == other.Weight;

    public override bool Equals(object? obj)
        => Equals(obj as VariantModel);

    public override int GetHashCode()
        => HashCode.Combine(Model, RotationX, RotationY, RotationZ, UVLock, Weight);
}