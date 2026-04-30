using SkiaSharp;

namespace Solace.TileRenderer.Wkb;

internal sealed class LinearRing
{
    public LinearRing(Point[] points)
    {
        Points = points;
    }

    public Point[] Points { get; }

    public static LinearRing Load(BinaryReader reader)
    {
        int numPoints = reader.ReadInt32();
        var points = new Point[numPoints];

        for (int i = 0; i < numPoints; i++)
        {
            points[i] = Point.Load(reader);
        }

        return new LinearRing(points);
    }

    public void Render(SKCanvas canvas, Tile tile, SKColor color, float strokeWidth)
    {
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.StrokeAndFill,
            StrokeWidth = strokeWidth,
            IsAntialias = false,
        };

        using var path = new SKPath();

        for (int i = 0; i < Points.Length; i++)
        {
            var pixelPoint = tile.ToLocalPixel(Points[i]);

            if (i == 0)
            {
                path.MoveTo((float)pixelPoint.X, (float)pixelPoint.Y); // Begin new polygon
            }
            else
            {
                path.LineTo((float)pixelPoint.X, (float)pixelPoint.Y);
            }
        }

        path.Close(); // Close the path to ensure polygon is sealed
        canvas.DrawPath(path, paint);
    }
}
