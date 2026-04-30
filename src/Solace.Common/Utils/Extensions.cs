namespace ViennaDotNet.Common.Utils;

public static class Extensions
{
    public static ValueTask<T?> AsJsonAsync<T>(this Stream stream, CancellationToken cancellationToken)
        => Json.DeserializeAsync<T>(stream, cancellationToken);

    public static async Task<string> ReadAsString(this Stream stream, CancellationToken cancellationToken = default)
    {
        using (StreamReader reader = new StreamReader(stream))
            return await reader.ReadToEndAsync(cancellationToken);
    }

    public static U? Map<T, U>(this T? value, Func<T, U> mapper)
    {
        if (value is null) return default;
        else return mapper(value);
    }

    //public static T? OrElse<T>(this T? value, T other)
    //{
    //    if (value is null)
    //        return other;
    //    else
    //        return value;
    //}
}
