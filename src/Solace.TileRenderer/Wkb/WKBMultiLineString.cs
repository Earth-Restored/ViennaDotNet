using SkiaSharp;

namespace Solace.TileRenderer.Wkb;

internal sealed class WKBMultiLineString : IWKBObject
{
    public WKBMultiLineString(bool byteOrder, uint wkbType, uint srid, WKBLineString[] wkbLineStrings)
    {
        ByteOrder = byteOrder;
        WkbType = wkbType;
        Srid = srid;
        WKBLineStrings = wkbLineStrings;
    }

    public bool ByteOrder { get; }

    public uint WkbType { get; }

    public uint Srid { get; }

    public WKBLineString[] WKBLineStrings { get; }

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

        int numWKBLineStrings = reader.ReadInt32();
        var wkbLineStrings = new WKBLineString[numWKBLineStrings];
        for (int i = 0; i < numWKBLineStrings; i++)
        {
            wkbLineStrings[i] = (WKBLineString)WKBLineString.Load(reader);
        }

        return new WKBMultiLineString(byteOrder, wkbType, srid, wkbLineStrings);
    }

    public void Render(SKCanvas canvas, Tile tile, SKColor color, float strokeWidth)
    {
        for (int i = 0; i < WKBLineStrings.Length; i++)
        {
            WKBLineStrings[i].Render(canvas, tile, color, strokeWidth);
        }
    }
}
