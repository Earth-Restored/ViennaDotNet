using System.Runtime.CompilerServices;

namespace ViennaDotNet.Common;

public static class MathE
{
    private const float PISingle = MathF.PI;
    private const double PIDouble = Math.PI;

    private const float DegToRadSingle = PISingle / 180f;
    private const double DegToRadDouble = PIDouble / 180.0;
    private const float RadToDegSingle = 180f / PISingle;
    private const double RadToDegDouble = 180.0 / PIDouble;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToRadians(float degrees)
        => degrees * DegToRadSingle;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToRadians(double degrees)
        => degrees * DegToRadDouble;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToDegrees(float degrees)
        => degrees * RadToDegSingle;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToDegrees(double degrees)
        => degrees * RadToDegDouble;
}
