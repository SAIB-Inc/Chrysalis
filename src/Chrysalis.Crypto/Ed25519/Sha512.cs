using Chrysalis.Crypto.Internal;

namespace Chrysalis.Crypto;

/// <summary>
/// Provides a managed SHA-512 hash implementation used internally by Ed25519 operations.
/// </summary>
internal sealed class Sha512
{
    private Array8<ulong> _state;
    private readonly byte[] _buffer;
    private ulong _totalBytes;

    /// <summary>
    /// The SHA-512 block size in bytes (128).
    /// </summary>
    internal const int BlockSize = 128;

    private static readonly byte[] Padding = [0x80];

    /// <summary>
    /// Initializes a new instance of the <see cref="Sha512"/> class.
    /// </summary>
    internal Sha512()
    {
        _buffer = new byte[BlockSize];
        Init();
    }

    /// <summary>
    /// Resets the hasher to its initial state so it can be reused.
    /// </summary>
    internal void Init()
    {
        Sha512Internal.Sha512Init(out _state);
        _totalBytes = 0;
    }

    /// <summary>
    /// Updates the hash computation with additional data.
    /// </summary>
    /// <param name="data">The input data buffer.</param>
    /// <param name="offset">The offset into the data buffer.</param>
    /// <param name="count">The number of bytes to process.</param>
    internal void Update(byte[] data, int offset, int count)
    {
        Array16<ulong> block;
        int bytesInBuffer = (int)_totalBytes & (BlockSize - 1);
        _totalBytes += (uint)count;

        if (_totalBytes >= ulong.MaxValue / 8)
            throw new InvalidOperationException("Too much data");

        // Fill existing buffer
        if (bytesInBuffer != 0)
        {
            int toCopy = Math.Min(BlockSize - bytesInBuffer, count);
            Buffer.BlockCopy(data, offset, _buffer, bytesInBuffer, toCopy);
            offset += toCopy;
            count -= toCopy;
            bytesInBuffer += toCopy;
            if (bytesInBuffer == BlockSize)
            {
                ByteIntegerConverter.Array16LoadBigEndian64(out block, _buffer, 0);
                Sha512Internal.Core(out _state, ref _state, ref block);
                CryptoBytes.InternalWipe(_buffer, 0, _buffer.Length);
                bytesInBuffer = 0;
            }
        }

        // Hash complete blocks without copying
        while (count >= BlockSize)
        {
            ByteIntegerConverter.Array16LoadBigEndian64(out block, data, offset);
            Sha512Internal.Core(out _state, ref _state, ref block);
            offset += BlockSize;
            count -= BlockSize;
        }

        // Copy remainder into buffer
        if (count > 0)
        {
            Buffer.BlockCopy(data, offset, _buffer, bytesInBuffer, count);
        }
    }

    /// <summary>
    /// Finalizes the hash computation and returns the 64-byte digest.
    /// </summary>
    /// <returns>The 64-byte SHA-512 hash result.</returns>
    internal byte[] Finish()
    {
        byte[] result = new byte[64];
        Finish(new ArraySegment<byte>(result));
        return result;
    }

    /// <summary>
    /// Computes the SHA-512 hash of the specified data.
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <param name="offset">The offset into the data buffer.</param>
    /// <param name="count">The number of bytes to hash.</param>
    /// <returns>The 64-byte SHA-512 hash result.</returns>
    internal static byte[] Hash(byte[] data, int offset, int count)
    {
        Sha512 hasher = new();
        hasher.Update(data, offset, count);
        return hasher.Finish();
    }

    private void Finish(ArraySegment<byte> output)
    {
        Update(Padding, 0, Padding.Length);
        Array16<ulong> block;
        ByteIntegerConverter.Array16LoadBigEndian64(out block, _buffer, 0);
        CryptoBytes.InternalWipe(_buffer, 0, _buffer.Length);
        int bytesInBuffer = (int)_totalBytes & (BlockSize - 1);
        if (bytesInBuffer > BlockSize - 16)
        {
            Sha512Internal.Core(out _state, ref _state, ref block);
            block = default;
        }
        block.x15 = (_totalBytes - 1) * 8;
        Sha512Internal.Core(out _state, ref _state, ref block);

        ByteIntegerConverter.StoreBigEndian64(output.Array!, output.Offset + 0, _state.x0);
        ByteIntegerConverter.StoreBigEndian64(output.Array!, output.Offset + 8, _state.x1);
        ByteIntegerConverter.StoreBigEndian64(output.Array!, output.Offset + 16, _state.x2);
        ByteIntegerConverter.StoreBigEndian64(output.Array!, output.Offset + 24, _state.x3);
        ByteIntegerConverter.StoreBigEndian64(output.Array!, output.Offset + 32, _state.x4);
        ByteIntegerConverter.StoreBigEndian64(output.Array!, output.Offset + 40, _state.x5);
        ByteIntegerConverter.StoreBigEndian64(output.Array!, output.Offset + 48, _state.x6);
        ByteIntegerConverter.StoreBigEndian64(output.Array!, output.Offset + 56, _state.x7);
        _state = default;
    }
}
