using System.Runtime.CompilerServices;

namespace Chrysalis.Plutus.Flat;

/// <summary>
/// Bit-level reader for Flat-encoded UPLC programs.
/// Reads bits MSB-first within each byte (bit 7 first, bit 0 last).
/// Optimized: caches the underlying byte array and uses bit-position tracking
/// to enable bulk reads without per-bit method calls.
/// </summary>
internal sealed class BitReader
{
    private readonly byte[] _bytes;
    private readonly int _length;
    private int _byteIndex;
    private int _bitsLeft; // bits remaining in current byte (8 = full byte, 0 = need next byte)
    private int _currentByte;

    internal BitReader(ReadOnlyMemory<byte> data)
    {
        // Extract backing array to avoid ReadOnlyMemory.Span overhead per bit read.
        // TryGetArray succeeds without copying when data is backed by byte[] (the common case).
        _bytes = System.Runtime.InteropServices.MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment)
            ? segment.Array!
            : data.ToArray();
        _length = segment.Offset + data.Length;
        _byteIndex = segment.Offset;
        _bitsLeft = 0;
        _currentByte = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureBits()
    {
        if (_bitsLeft == 0)
        {
            if (_byteIndex >= _length)
            {
                ThrowPastEnd();
            }
            _currentByte = _bytes[_byteIndex++];
            _bitsLeft = 8;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int PopBit()
    {
        EnsureBits();
        _bitsLeft--;
        return (_currentByte >> _bitsLeft) & 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int PopBits(int count)
    {
        // Fast path: all bits fit in current byte
        if (_bitsLeft >= count)
        {
            _bitsLeft -= count;
            return (_currentByte >> _bitsLeft) & ((1 << count) - 1);
        }

        // Slow path: spans byte boundary
        return PopBitsSlow(count);
    }

    private int PopBitsSlow(int count)
    {
        int value = 0;
        int remaining = count;

        // Drain current byte
        if (_bitsLeft > 0)
        {
            value = _currentByte & ((1 << _bitsLeft) - 1);
            remaining -= _bitsLeft;
            _bitsLeft = 0;
        }

        // Read full bytes
        while (remaining >= 8)
        {
            if (_byteIndex >= _length)
            {
                ThrowPastEnd();
            }
            value = (value << 8) | _bytes[_byteIndex++];
            remaining -= 8;
        }

        // Read remaining bits from next byte
        if (remaining > 0)
        {
            if (_byteIndex >= _length)
            {
                ThrowPastEnd();
            }
            _currentByte = _bytes[_byteIndex++];
            _bitsLeft = 8 - remaining;
            value = (value << remaining) | (_currentByte >> _bitsLeft);
        }

        return value;
    }

    internal byte PopByte()
    {
        // If byte-aligned, read directly
        if (_bitsLeft == 0)
        {
            if (_byteIndex >= _length)
            {
                ThrowPastEnd();
            }
            return _bytes[_byteIndex++];
        }

        // If not aligned, discard remaining bits and read next byte
        _bitsLeft = 0;
        if (_byteIndex >= _length)
        {
            ThrowPastEnd();
        }
        return _bytes[_byteIndex++];
    }

    internal ReadOnlyMemory<byte> TakeBytes(int count)
    {
        if (_bitsLeft != 0)
        {
            // Discard partial byte — caller expects byte-aligned
            _bitsLeft = 0;
        }

        if (_byteIndex + count > _length)
        {
            ThrowPastEnd();
        }

        ReadOnlyMemory<byte> slice = new(_bytes, _byteIndex, count);
        _byteIndex += count;
        return slice;
    }

    internal void SkipPadding()
    {
        while (PopBit() == 0)
        {
            // skip padding zeros
        }
        // The terminating 1-bit has been consumed
    }

    private static void ThrowPastEnd() => throw new InvalidOperationException("Flat: read past end of input.");
}
