namespace ViennaDotNet.Common.Utils;

public static class ArrayExtensions
{
    public static T[] CopyOfRange<T>(T[] src, int start, int end)
    {
        int len = end - start;
        T[] dest = new T[len];
        Array.ConstrainedCopy(src, start, dest, 0, len);
        return dest;
    }

    public static T[] SubArray<T>(this T[] array, int start, int length)
    {
        T[] newArary = new T[length];
        Array.ConstrainedCopy(array, start, newArary, 0, length);
        return newArary;
    }
}
