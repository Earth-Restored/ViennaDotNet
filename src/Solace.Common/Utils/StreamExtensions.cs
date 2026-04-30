namespace Solace.Common.Utils;

public static class StreamExtensions
{
    public static ValueTask<T?> AsJsonAsync<T>(this Stream stream, CancellationToken cancellationToken)
        => Json.DeserializeAsync<T>(stream, cancellationToken);

    public static async Task<string> ReadAsString(this Stream stream, CancellationToken cancellationToken = default)
    {
        using (var reader = new StreamReader(stream))
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }
}
