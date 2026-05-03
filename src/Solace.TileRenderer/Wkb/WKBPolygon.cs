using SkiaSharp;

namespace Solace.TileRenderer.Wkb;

internal sealed class WKBPolygon : IWKBObject
{
    public WKBPolygon(bool byteOrder, uint wkbType, uint srid, LinearRing[] rings)
    {
        ByteOrder = byteOrder;
        WkbType = wkbType;
        Srid = srid;
        Rings = rings;
    }

    public bool ByteOrder { get; }

    public uint WkbType { get; }

    public uint Srid { get; }

    public LinearRing[] Rings { get; }

    public static IWKBObject Load(BinaryReader reader)
    {
        bool byteOrder = reader.ReadByte() == 1;
        if (byteOrder != BitConverter.IsLittleEndian)
        {
            throw new NotImplementedException();
        }

        uint wkbType = reader.ReadUInt32();

        uint srid = 0;
        if ((wkbType & Constants.WkbSRID) != 0)
        {
            srid = reader.ReadUInt32();
        }

        int numRings = reader.ReadInt32();
        var rings = new LinearRing[numRings];
        for (int i = 0; i < numRings; i++)
        {
            rings[i] = LinearRing.Load(reader);
        }

        return new WKBPolygon(byteOrder, wkbType, srid, rings);
    }

    public void Render(SKCanvas canvas, Tile tile, SKColor color, float strokeWidth)
    {
        for (int i = 0; i < Rings.Length; i++)
        {
            Rings[i].Render(canvas, tile, color, strokeWidth);
        }
    }
}
