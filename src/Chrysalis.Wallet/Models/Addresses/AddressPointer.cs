namespace Chrysalis.Wallet.Models.Addresses;

/// <summary>
/// Represents a CIP-0019 stake-registration pointer encoded as three canonical big-endian base-128 VLQ integers.
/// </summary>
/// <param name="Slot">The slot number containing the stake registration certificate.</param>
/// <param name="TransactionIndex">The zero-based transaction index within the block.</param>
/// <param name="CertificateIndex">The zero-based certificate index within the transaction.</param>
public readonly record struct AddressPointer(ulong Slot, ulong TransactionIndex, ulong CertificateIndex)
{
    internal int EncodedByteLength =>
        GetVarUIntLength(Slot) + GetVarUIntLength(TransactionIndex) + GetVarUIntLength(CertificateIndex);

    /// <summary>Encodes this pointer into its canonical big-endian 7-bit VLQ byte representation.</summary>
    /// <returns>The canonical variable-length pointer bytes.</returns>
    public byte[] ToBytes()
    {
        byte[] bytes = new byte[EncodedByteLength];
        _ = TryWriteBytes(bytes, out _);
        return bytes;
    }

    /// <summary>Attempts to write this pointer's canonical VLQ bytes into <paramref name="destination"/>.</summary>
    /// <param name="destination">The buffer to write into; left unmodified if too short.</param>
    /// <param name="bytesWritten">When this returns <see langword="true"/>, the bytes written; otherwise 0.</param>
    /// <returns><see langword="true"/> if the pointer fit in <paramref name="destination"/>; otherwise <see langword="false"/>.</returns>
    public bool TryWriteBytes(Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < EncodedByteLength)
        {
            bytesWritten = 0;
            return false;
        }

        int offset = 0;
        WriteVarUInt(Slot, destination, ref offset);
        WriteVarUInt(TransactionIndex, destination, ref offset);
        WriteVarUInt(CertificateIndex, destination, ref offset);
        bytesWritten = offset;
        return true;
    }

    /// <summary>Decodes a canonical CIP-0019 variable-length pointer from <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The exact canonical variable-length pointer bytes with no trailing data.</param>
    /// <returns>The decoded <see cref="AddressPointer"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not a valid canonical pointer encoding.</exception>
    public static AddressPointer FromBytes(ReadOnlySpan<byte> bytes) =>
        TryFromBytes(bytes, out AddressPointer result)
            ? result
            : throw new ArgumentException("Invalid canonical pointer encoding.", nameof(bytes));

    /// <summary>Attempts to decode a canonical CIP-0019 variable-length pointer from <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The exact canonical variable-length pointer bytes with no trailing data.</param>
    /// <param name="result">When this returns <see langword="true"/>, the decoded pointer; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="bytes"/> is a valid canonical pointer encoding; otherwise <see langword="false"/>.</returns>
    public static bool TryFromBytes(ReadOnlySpan<byte> bytes, out AddressPointer result)
    {
        int offset = 0;
        if (!TryReadVarUInt(bytes, ref offset, out ulong slot)
            || !TryReadVarUInt(bytes, ref offset, out ulong txIndex)
            || !TryReadVarUInt(bytes, ref offset, out ulong certIndex)
            || offset != bytes.Length)
        {
            result = default;
            return false;
        }

        result = new AddressPointer(slot, txIndex, certIndex);
        return true;
    }

    private static int GetVarUIntLength(ulong value)
    {
        int len = 1;
        while (value >= 0x80UL)
        {
            value >>= 7;
            len++;
        }

        return len;
    }

    private static void WriteVarUInt(ulong value, Span<byte> destination, ref int offset)
    {
        int len = GetVarUIntLength(value);
        destination[offset + len - 1] = (byte)(value & 0x7FUL);
        ulong remaining = value >> 7;
        for (int i = len - 2; i >= 0; i--)
        {
            destination[offset + i] = (byte)((remaining & 0x7FUL) | 0x80UL);
            remaining >>= 7;
        }

        offset += len;
    }

    private static bool TryReadVarUInt(ReadOnlySpan<byte> bytes, ref int offset, out ulong value)
    {
        if (offset >= bytes.Length) { value = 0; return false; }

        byte b = bytes[offset++];
        if (b == 0x80) { value = 0; return false; }

        ulong acc = (ulong)(b & 0x7F);
        while ((b & 0x80) != 0)
        {
            if (acc > (ulong.MaxValue >> 7) || offset >= bytes.Length) { value = 0; return false; }
            b = bytes[offset++];
            acc = (acc << 7) | (uint)(b & 0x7F);
        }

        value = acc;
        return true;
    }
}
