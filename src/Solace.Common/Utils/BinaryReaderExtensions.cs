using System.Buffers.Binary;

namespace ViennaDotNet.Common.Utils;

public static class BinaryReaderExtensions
{
    public static uint ReadUInt32BE(this BinaryReader reader)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        int read = reader.Read(buffer);
        if (read != sizeof(uint))
        {
            throw new EndOfStreamException($"{sizeof(uint)} bytes required from stream, but only {read} returned.");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }
}
