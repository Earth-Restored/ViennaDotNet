using System.Buffers.Text;
using System.Text;

namespace ViennaDotNet.Launcher.Client.Utils;

public static class DataUtils
{
    public static unsafe string DataToUri(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return "data:text/plain;base64,";
        }

        const string Prefix = "data:text/plain;base64,";

        int base64Length = ((data.Length + 2) / 3) * 4;
        int totalLength = Prefix.Length + base64Length;

        fixed (byte* ptr = data)
        {
            var state = ((IntPtr)ptr, data.Length);

            return string.Create(totalLength, state, static (span, s) =>
            {
                Prefix.AsSpan().CopyTo(span);

                var byteSpan = new ReadOnlySpan<byte>((void*)s.Item1, s.Item2);

                Convert.TryToBase64Chars(byteSpan, span[Prefix.Length..], out _);
            });
        }
    }
}