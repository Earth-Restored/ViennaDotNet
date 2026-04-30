using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using BitcoderCZ.Maths.Vectors;

namespace ViennaDotNet.BuildplateRenderer.Models.ResourcePacks;

public sealed class BlockModel
{
    public required IReadOnlyDictionary<string, DisplayPart> Display { get; init; }

    // texture variable to texture
    public IReadOnlyDictionary<string, string>? Textures { get; init; }

    public required ImmutableArray<BlockElement> Elements { get; init; }
}

// https://minecraft.wiki/w/Model#Block_models
public sealed class BlockModelJson
{
    public string? Parent { get; init; }

    public Dictionary<string, DisplayPart>? Display { get; init; }

    // texture variable to texture
    public Dictionary<string, string>? Textures { get; init; }

    public BlockElementJson[]? Elements { get; init; }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct DisplayPart
{
    public Vector3 Rotation { get; init; }

    public Vector3 Translation { get; init; }

    public Vector3 Scale { get; init; }
}

public sealed class BlockElementJson
{
    public Vector3 From { get; init; }

    public Vector3 To { get; init; }

    public BlockElementRotationJson? Rotation { get; init; }

    public bool Shade { get; init; }

    [JsonPropertyName("light_emission")]
    public int LightEmission { get; init; }

    public BlockElementFacesJson Faces { get; init; }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct BlockElementRotationJson
{
    // the center of the rotation
    public Vector3 Origin { get; init; }

    // whether or not to scale the faces across the whole block by scaling the non-rotated faces by 1 / cos(angle)
    [JsonPropertyName("rescale")]
    public bool ReScale { get; init; }

    // either all angles
    // in degrees
    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }

    // or axis
    public Axis? Axis { get; init; }

    // in degrees
    public float? Angle { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Axis
{
    [JsonStringEnumMemberName("x")] X,
    [JsonStringEnumMemberName("y")] Y,
    [JsonStringEnumMemberName("z")] Z
}

public sealed class BlockElement
{
    // 0 to 15 -> 0 to 1
    public Vector3 From { get; init; }

    // 0 to 15 -> 0 to 1
    public Vector3 To { get; init; }

    public BlockElementRotation? Rotation { get; init; }

    public bool Shade { get; init; }

    // 0 - 15
    public int LightEmission { get; init; }

    public BlockElementFaces Faces { get; init; }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct BlockElementRotation
{
    // the center of the rotation
    public Vector3 Origin { get; init; }

    // whether or not to scale the faces across the whole block by scaling the non-rotated faces by 1 / cos(angle)
    [JsonPropertyName("rescale")]
    public bool ReScale { get; init; }

    // in degrees
    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct BlockElementFacesJson
{
    public BlockFaceJson? Down { get; init; }

    public BlockFaceJson? Up { get; init; }

    public BlockFaceJson? North { get; init; }

    public BlockFaceJson? South { get; init; }

    public BlockFaceJson? West { get; init; }

    public BlockFaceJson? East { get; init; }
}

[InlineArray(6)]
public struct BlockElementFaces
{
    private BlockFace? _element0;
}

public sealed class BlockFaceJson
{
    [JsonPropertyName("uv")]
    public UVCoordinates? UV { get; init; }

    // not the final texture, but the texture variable
    public required string Texture { get; init; }

    [JsonPropertyName("cullface")]
    public DirectionJson? CullFace { get; init; }

    public int Rotation { get; init; }

    [JsonPropertyName("tintindex")]
    public int TintIndex { get; init; } = -1;
}

public sealed class BlockFace
{
    // 0 to 15 -> 0 to 1
    public UVCoordinates UV { get; init; }

    // not the final texture, but the texture variable
    public required string Texture { get; init; }

    public Direction? CullFace { get; init; }

    public int Rotation { get; init; }

    public int TintIndex { get; init; } = -1;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DirectionJson
{
    [JsonStringEnumMemberName("down")] Down,
    [JsonStringEnumMemberName("bottom")] Bottom,
    [JsonStringEnumMemberName("up")] Up,
    [JsonStringEnumMemberName("top")] Top,
    [JsonStringEnumMemberName("north")] North,
    [JsonStringEnumMemberName("south")] South,
    [JsonStringEnumMemberName("west")] West,
    [JsonStringEnumMemberName("east")] East,
}

// +X, -X, +Y, -Y, +Z, -Z
public enum Direction
{
    East = 0,
    West = 1,
    Up = 2,
    Down = 3,
    South = 4,
    North = 5,
}

[StructLayout(LayoutKind.Auto)]
public readonly struct UVCoordinates
{
    public UVCoordinates(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public UVCoordinates(float x1, float y1, float x2, float y2)
    {
        Min = new Vector2(x1, y1);
        Max = new Vector2(x2, y2);
    }

    public Vector2 Min { get; init; }

    public Vector2 Max { get; init; }
}