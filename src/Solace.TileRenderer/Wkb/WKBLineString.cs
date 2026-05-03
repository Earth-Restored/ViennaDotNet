using SkiaSharp;

namespace Solace.TileRenderer.Wkb;

internal sealed class WKBLineString : IWKBObject
{
    public WKBLineString(bool byteOrder, uint wkbType, uint srid, Point[] points)
    {
        ByteOrder = byteOrder;
        WkbType = wkbType;
        Srid = srid;
        Points = points;
    }

    public bool ByteOrder { get; } // 1=little-endian
    public uint WkbType { get; }
    public uint Srid { get; }
    public Point[] Points { get; }

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

        int numPoints = reader.ReadInt32();
        var points = new Point[numPoints];
        for (int i = 0; i < numPoints; i++)
        {
            points[i] = Point.Load(reader);
        }

        return new WKBLineString(byteOrder, wkbType, srid, points);
    }

    public void Render(SKCanvas canvas, Tile tile, SKColor color, float strokeWidth)
    {
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            IsAntialias = false,
        };

        using var path = new SKPath();

        for (int i = 0; i < Points.Length; i++)
        {
            var pixelPoint = tile.ToLocalPixel(Points[i]);

            if (i == 0)
            {
                path.MoveTo((float)pixelPoint.X, (float)pixelPoint.Y);
            }
            else
            {
                path.LineTo((float)pixelPoint.X, (float)pixelPoint.Y);
            }
        }

        canvas.DrawPath(path, paint);
    }
}
