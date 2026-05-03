using SkiaSharp;

namespace Solace.TileRenderer.Wkb;

internal sealed class WKBMultiPolygon : IWKBObject
{
    public WKBMultiPolygon(bool byteOrder, uint wkbType, uint srid, WKBPolygon[] wkbPolygons)
    {
        ByteOrder = byteOrder;
        WkbType = wkbType;
        Srid = srid;
        WKBPolygons = wkbPolygons;
    }

    public bool ByteOrder { get; }

    public uint WkbType { get; }

    public uint Srid { get; }

    public WKBPolygon[] WKBPolygons { get; }

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

        int numWKBPolygons = reader.ReadInt32();
        var wkbPolygons = new WKBPolygon[numWKBPolygons];
        for (int i = 0; i < numWKBPolygons; i++)
        {
            wkbPolygons[i] = (WKBPolygon)WKBPolygon.Load(reader);
        }

        return new WKBMultiPolygon(byteOrder, wkbType, srid, wkbPolygons);
    }

    public void Render(SKCanvas canvas, Tile tile, SKColor color, float strokeWidth)
    {
        for (int i = 0; i < WKBPolygons.Length; i++)
        {
            WKBPolygons[i].Render(canvas, tile, color, strokeWidth);
        }
    }
}
