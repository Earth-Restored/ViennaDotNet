using System.Text.Json.Serialization;

namespace ViennaDotNet.PreviewGenerator;

internal sealed record PreviewModel(
     [property: JsonPropertyName("format_version")] int FormatVersion, // always 1
     bool IsNight,
     [property: JsonPropertyName("sub_chunks")] PreviewModel.SubChunk[] SubChunks,
     PreviewModel.BlockEntity[] BlockEntities,
     PreviewModel.Entity[] Entities
)
{
    public sealed record Position(
        int X,
        int Y,
        int Z
    );

    public sealed record SubChunk(
        Position Position,
        [property: JsonPropertyName("block_palette")] SubChunk.PaletteEntry[] BlockPalette,
        int[] Blocks
    )
    {
        public sealed record PaletteEntry(
            string Name,
            int Data
        );
    }

    public sealed record BlockEntity(
        int Type,
        Position Position,
        JsonNbtConverter.JsonNbtTag Data
    );

    public sealed record Entity(
        string Name,
        Entity.PositionR Position,
        Entity.RotationR Rotation,
        Entity.PositionR ShadowPosition,
        float ShadowSize,
        int OverlayColor,
        int ChangeColor,
        int MultiplicitiveTintChangeColor,
        Dictionary<string, object>? ExtraData,
        string SkinData,
        bool IsPersonaSkin
    )
    {
        public sealed record PositionR(
            float X,
            float Y,
            float Z
        );

        public sealed record RotationR(
            float X,
            float Y
        );
    }
}
