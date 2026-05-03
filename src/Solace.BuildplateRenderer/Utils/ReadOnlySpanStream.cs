using System.Diagnostics;

namespace Solace.BuildplateRenderer.Utils;

// Adapted from MemoryStream implementation in .NET Runtime.
// Copyright (c) .NET Foundation and Contributors. Licensed under the MIT License.
// https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/MemoryStream.cs
internal sealed class ReadOnlySpanStream : Stream
{
    private const int SpanStreamMaxLength = int.MaxValue;

    private readonly ReadOnlyMemory<byte> _buffer;
    private int _position;
    private readonly int _length;
    private bool _isOpen;

    public ReadOnlySpanStream(ReadOnlyMemory<byte> buffer)
    {
        _buffer = buffer;
        _length = _buffer.Length;
        _isOpen = true;
    }

    public override bool CanRead => _isOpen;

    public override bool CanSeek => _isOpen;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            EnsureNotClosed();
            return _length;
        }
    }

    public override long Position
    {
        get
        {
            EnsureNotClosed();
            return _position;
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            EnsureNotClosed();

            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, SpanStreamMaxLength);

            _position = (int)value;
        }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        EnsureNotClosed();

        var bufferSpan = _buffer.Span;

        int n = _length - _position;
        if (n > count)
        {
            n = count;
        }

        if (n <= 0)
        {
            return 0;
        }

        Debug.Assert(_position + n >= 0, "_position + n >= 0");  // len is less than 2^31 -1.

        if (n <= 8)
        {
            int byteCount = n;
            while (--byteCount >= 0)
            {
                buffer[offset + byteCount] = bufferSpan[_position + byteCount];
            }
        }
        else
        {
            bufferSpan.Slice(_position, n).CopyTo(buffer.AsSpan(offset, n));
        }

        _position += n;

        return n;
    }

    public override int Read(Span<byte> buffer)
    {
        EnsureNotClosed();

        int n = Math.Min(_length - _position, buffer.Length);
        if (n <= 0)
        {
            return 0;
        }

        _buffer.Span.Slice(_position, n).CopyTo(buffer);

        _position += n;
        return n;
    }

    public override int ReadByte()
    {
        EnsureNotClosed();

        return _position < _length
            ? _buffer.Span[_position++]
            : -1;
    }

    public override void CopyTo(Stream destination, int bufferSize)
    {
        // Validate the arguments the same way Stream does for back-compat.
        ValidateCopyToArguments(destination, bufferSize);
        EnsureNotClosed();

        int originalPosition = _position;

        // Seek to the end of the ReadOnlySpanStream.
        int remaining = InternalEmulateRead(_length - originalPosition);

        // If we were already at or past the end, there's no copying to do so just quit.
        if (remaining > 0)
        {
            // Call Write() on the other Stream, using our internal buffer and avoiding any
            // intermediary allocations.
            destination.Write(_buffer.Span.Slice(originalPosition, remaining));
        }
    }

    public override long Seek(long offset, SeekOrigin loc)
    {
        EnsureNotClosed();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, SpanStreamMaxLength);

        switch (loc)
        {
            case SeekOrigin.Begin:
                {
                    int tempPosition = unchecked((int)offset);
                    if (offset < 0 || tempPosition < 0)
                    {
                        throw new IOException("An attempt was made to move the position before the beginning of the stream.");
                    }

                    _position = tempPosition;
                    break;
                }

            case SeekOrigin.Current:
                {
                    int tempPosition = unchecked(_position + (int)offset);
                    if (unchecked(_position + offset) < 0 || tempPosition < 0)
                    {
                        throw new IOException("An attempt was made to move the position before the beginning of the stream.");
                    }

                    _position = tempPosition;
                    break;
                }

            case SeekOrigin.End:
                {
                    int tempPosition = unchecked(_length + (int)offset);
                    if (unchecked(_length + offset) < 0 || tempPosition < 0)
                    {
                        throw new IOException("An attempt was made to move the position before the beginning of the stream.");
                    }

                    _position = tempPosition;
                    break;
                }

            default:
                throw new ArgumentException("Invalid seek origin.", nameof(loc));
        }

        Debug.Assert(_position >= 0, "_position >= 0");
        return _position;
    }

    public override void SetLength(long value)
    {
        if (value < 0 || value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Stream length must be non-negative and less than 2^31 - 1 - origin.");
        }

        throw new NotSupportedException($"{nameof(ReadOnlySpanStream)}'s length is fixed.");
    }

    public byte[] ToArray()
    {
        int count = _length;
        if (count == 0)
        {
            return [];
        }

        byte[] copy = GC.AllocateUninitializedArray<byte>(count);
        _buffer.Span[..count].CopyTo(copy);
        return copy;
    }

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException($"{nameof(ReadOnlySpanStream)}'s length is fixed.");

    public override void Write(ReadOnlySpan<byte> buffer)
        => throw new NotSupportedException($"{nameof(ReadOnlySpanStream)}'s length is fixed.");

    public override void WriteByte(byte value)
        => throw new NotSupportedException($"{nameof(ReadOnlySpanStream)}'s length is fixed.");

    // Writes this ReadOnlySpanStream to another stream.
    public void WriteTo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        EnsureNotClosed();

        stream.Write(_buffer.Span[.._length]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _isOpen = false;
        }
    }

    private int InternalEmulateRead(int count)
    {
        EnsureNotClosed();

        int n = _length - _position;
        if (n > count)
        {
            n = count;
        }

        if (n < 0)
        {
            n = 0;
        }

        Debug.Assert(_position + n >= 0, "_position + n >= 0");  // len is less than 2^31 -1.
        _position += n;
        return n;
    }

    private void EnsureNotClosed()
        => ObjectDisposedException.ThrowIf(!_isOpen, this);
}