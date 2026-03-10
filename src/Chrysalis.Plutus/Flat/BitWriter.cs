using System.Buffers;

namespace Chrysalis.Plutus.Flat;

/// <summary>
/// Bit-level writer for Flat-encoded UPLC programs.
/// Writes bits MSB-first within each byte.
/// Uses ArrayBufferWriter for efficient resizable output.
/// </summary>
internal sealed class BitWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private int _currentByte;
    private int _bitIndex;

    internal void PushBit(int bit)
    {
        _currentByte = (_currentByte << 1) | (bit & 1);
        _bitIndex++;

        if (_bitIndex == 8)
        {
            FlushByte();
        }
    }

    internal void PushBits(int value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            PushBit((value >> i) & 1);
        }
    }

    internal void PushByte(byte value)
    {
        if (_bitIndex != 0)
        {
            throw new InvalidOperationException("Flat: pushByte called when not byte-aligned.");
        }

        Span<byte> span = _buffer.GetSpan(1);
        span[0] = value;
        _buffer.Advance(1);
    }

    internal void PushBytes(ReadOnlySpan<byte> bytes)
    {
        if (_bitIndex != 0)
        {
            throw new InvalidOperationException("Flat: pushBytes called when not byte-aligned.");
        }

        Span<byte> span = _buffer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        _buffer.Advance(bytes.Length);
    }

    internal void Pad()
    {
        if (_bitIndex == 0)
        {
            // Already aligned — push a full padding byte (00000001)
            PushByte(1);
            return;
        }

        // Fill remaining bits with 0s, then a trailing 1
        while (_bitIndex < 7)
        {
            PushBit(0);
        }
        PushBit(1);
    }

    internal ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;

    internal byte[] ToArray()
    {
        return _buffer.WrittenSpan.ToArray();
    }

    private void FlushByte()
    {
        Span<byte> span = _buffer.GetSpan(1);
        span[0] = (byte)_currentByte;
        _buffer.Advance(1);
        _currentByte = 0;
        _bitIndex = 0;
    }
}
