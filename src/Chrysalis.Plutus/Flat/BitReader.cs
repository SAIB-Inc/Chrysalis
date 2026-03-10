namespace Chrysalis.Plutus.Flat;

/// <summary>
/// Bit-level reader for Flat-encoded UPLC programs.
/// Reads bits MSB-first within each byte (bit 7 first, bit 0 last).
/// Works over ReadOnlyMemory to avoid unnecessary copying.
/// </summary>
internal sealed class BitReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _byteIndex;
    private int _bitMask = 128; // MSB first

    internal BitReader(ReadOnlyMemory<byte> data) => _data = data;

    internal int PopBit()
    {
        if (_bitMask < 1)
        {
            _bitMask = 128;
            _byteIndex++;
        }

        ReadOnlySpan<byte> span = _data.Span;
        if (_byteIndex >= span.Length)
        {
            throw new InvalidOperationException("Flat: popBit past end of input.");
        }

        int result = (span[_byteIndex] & _bitMask) != 0 ? 1 : 0;
        _bitMask >>= 1;
        return result;
    }

    internal int PopBits(int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
        {
            value = (value << 1) | PopBit();
        }
        return value;
    }

    internal byte PopByte()
    {
        if (_bitMask != 128)
        {
            _bitMask = 128;
            _byteIndex++;
        }

        ReadOnlySpan<byte> span = _data.Span;
        if (_byteIndex >= span.Length)
        {
            throw new InvalidOperationException("Flat: popByte past end of input.");
        }

        byte result = span[_byteIndex];
        _byteIndex++;
        return result;
    }

    internal ReadOnlyMemory<byte> TakeBytes(int count)
    {
        if (_bitMask != 128)
        {
            throw new InvalidOperationException("Flat: takeBytes called when not byte-aligned.");
        }

        if (_byteIndex + count > _data.Length)
        {
            throw new InvalidOperationException("Flat: takeBytes past end of input.");
        }

        ReadOnlyMemory<byte> slice = _data.Slice(_byteIndex, count);
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
}
