using SkiaSharp;

namespace ViennaDotNet.TileRenderer.Wkb;

public interface IWKBObject
{
    bool ByteOrder { get; }

    uint WkbType { get; }

    static virtual IWKBObject Load(BinaryReader reader)
        => throw new NotImplementedException();

    void Render(SKCanvas canvas, Tile tile, SKColor color, float strokeWidth);
}
