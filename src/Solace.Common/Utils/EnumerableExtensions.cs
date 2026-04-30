namespace Solace.Common.Utils;

public static class EnumerableExtensions
{
    public static bool IsEmpty<T>(this IEnumerable<T> enumerable)
        => !enumerable.Any();

    public static R Collect<T, R>(this IEnumerable<T> enumerable, Func<R> supplier, Action<R, T> accumulator, Action<R, R> combiner)
    {
        R result = supplier();

        foreach (T item in enumerable)
            accumulator(result, item);

        // TODO: use combiner???
        return result;
    }
}
