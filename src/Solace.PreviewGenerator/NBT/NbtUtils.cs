using System.Text;

namespace Solace.PreviewGenerator.NBT;

public static class NbtUtils
{
    public static readonly int MaxDepth = 16;
    public static readonly long MaxReadSize; // Disabled by default

    public static string ToString(object o)
    {
        if (o is byte @byte)
        {
            return @byte + "b";
        }
        else if (o is short @short)
        {
            return @short + "s";
        }
        else if (o is int @int)
        {
            return @int + "i";
        }
        else if (o is long @long)
        {
            return @long + "l";
        }
        else if (o is float @float)
        {
            return @float + "f";
        }
        else if (o is double @double)
        {
            return @double + "d";
        }
        else if (o is byte[] byteArr)
        {
            return "0x" + ToHexString(byteArr);
        }
        else if (o is string @string)
        {
            return "\"" + @string + "\"";
        }
        else if (o is int[] intArr)
        {
            return "[ " + string.Join(", ", intArr.Select(i => i + "i")) + " ]";
        }
        else if (o is long[] longArr)
        {
            return "[ " + string.Join(", ", longArr.Select(l => l + "l")) + " ]";
        }

        return o.ToString()!;
    }

    public static object Copy(object val)
    {
        if (val is byte[] byteArr)
        {
            return byteArr.Clone();
        }
        else if (val is int[] intArr)
        {
            return intArr.Clone();
        }
        else if (val is long[] longArr)
        {
            return longArr.Clone();
        }

        return val;
    }

    public static string Indent(string str)
    {
        var builder = new StringBuilder("  " + str);
        for (int i = 2; i < builder.Length; i++)
        {
            if (builder[i] == '\n')
            {
                builder.Insert(i + 1, "  ");
                i += 2;
            }
        }

        return builder.ToString();
    }

    private static readonly string HEX_CODE = "0123456789ABCDEF";

    public static string ToHexString(byte[] data)
    {
        var r = new StringBuilder(data.Length << 1);
        foreach (byte b in data)
        {
            r.Append(HEX_CODE[(b >> 4) & 0xF]);
            r.Append(HEX_CODE[b & 0xF]);
        }

        return r.ToString();
    }
}
