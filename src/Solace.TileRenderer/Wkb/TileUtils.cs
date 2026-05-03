using System.Runtime.CompilerServices;

namespace Solace.TileRenderer.Wkb;

internal static class TileUtils
{
    private const double Pi = double.Pi;
    private const double EarthRadius = 6378137;

    // All functions operate in degrees
    public static Point LonLatToSlippy(Point lonLat, int zoom)
    {
        double latRad = DegToRad(lonLat.Y);
        return new Point(
            (lonLat.X + 180.0) / 360.0 * (1 << zoom),
            (1.0 - double.Asinh(double.Tan(latRad)) / Pi) / 2.0 * (1 << zoom)
        );
    }

    public static Point SlippyTolonLat(Point slippy, int zoom)
    {
        double n = Pi - 2.0 * Pi * slippy.Y / (1 << zoom);
        return new Point(
            slippy.X / (1 << zoom) * 360.0 - 180,
            180.0 / Pi * double.Atan(0.5 * (double.Exp(n) - double.Exp(-n)))
        );
    }

    public static Point LonLatToSphereMerc(Point lonLat)
        => new Point(
            DegToRad(lonLat.X) * EarthRadius,
            double.Log(double.Tan(DegToRad(lonLat.Y) / 2 + (Pi / 4))) * EarthRadius
        );

    public static Point SphereMercToLonLat(Point sphereMerc)
        => new Point(
            RadToDeg(sphereMerc.X / EarthRadius),
            RadToDeg(2 * double.Atan(double.Exp(sphereMerc.Y / EarthRadius)) - Pi / 2)
        );

    public static Point SphereMercToSlippy(Point sphereMerc, int zoom)
        => LonLatToSlippy(SphereMercToLonLat(sphereMerc), zoom);

    public static Point SlippyToSphereMerc(Point slippy, int zoom)
        => LonLatToSphereMerc(SlippyTolonLat(slippy, zoom));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DegToRad(double numb)
        => numb / (180d / Pi);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double RadToDeg(double numb)
        => numb * (180d / Pi);
}
