using System.Globalization;

namespace ViennaDotNet.TileRenderer.Wkb;

public struct Point
{
    public double X;
    public double Y;

    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }

    public Point(BinaryReader reader)
    {
        this = Load(reader);
    }

    public static Point Load(BinaryReader reader)
        => new Point(reader.ReadDouble(), reader.ReadDouble());

    public static Point operator +(Point left, Point right)
        => new Point(left.X + right.X, left.Y + right.Y);

    public static Point operator -(Point left, Point right)
        => new Point(left.X - right.X, left.Y - right.Y);

    public static Point operator *(Point left, Point right)
        => new Point(left.X * right.X, left.Y * right.Y);

    public static Point operator /(Point left, Point right)
        => new Point(left.X / right.X, left.Y / right.Y);

    public static Point operator +(Point left, double right)
        => new Point(left.X + right, left.Y + right);

    public static Point operator -(Point left, double right)
        => new Point(left.X - right, left.Y - right);

    public static Point operator *(Point left, double right)
        => new Point(left.X * right, left.Y * right);

    public static Point operator /(Point left, double right)
        => new Point(left.X / right, left.Y / right);

    public readonly override string ToString()
        => $"<{X.ToString("G", CultureInfo.InvariantCulture)}, {Y.ToString("G", CultureInfo.InvariantCulture)}>";
}
