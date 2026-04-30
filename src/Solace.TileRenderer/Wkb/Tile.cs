namespace ViennaDotNet.TileRenderer.Wkb;

public sealed class Tile
{
    public Tile(Point slippy, int zoom, int resolution)
    {
        Slippy = slippy;
        Zoom = zoom;
        Resolution = resolution;
    }

    public Point Slippy { get; }

    public int Zoom { get; }

    public int Resolution { get; }

    public Point ToLocalPixel(Point sphereMerc)
    {
        Point slippy = TileUtils.SphereMercToSlippy(sphereMerc, Zoom);
        slippy -= Slippy;
        slippy *= Resolution;

        return slippy;
    }
}
