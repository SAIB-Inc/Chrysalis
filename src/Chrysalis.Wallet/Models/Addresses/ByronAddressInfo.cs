using Chrysalis.Wallet.Models.Enums;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Wallet.Models.Addresses;

/// <summary>Represents the decoded read-only metadata of a Byron-era bootstrap address.</summary>
public sealed class ByronAddressInfo
{
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    /// <summary>Gets the 28-byte Byron address root hash.</summary>
    public ReadOnlyMemory<byte> Root { get; }

    /// <summary>Gets the Byron address type tag.</summary>
    public ByronAddressType AddressType { get; }

    /// <summary>Gets the verified IEEE 802.3 CRC-32 checksum of the inner CBOR payload.</summary>
    public uint Crc32 { get; }

    /// <summary>Gets the optional protocol network magic from attribute key 2, or <see langword="null"/> for Mainnet.</summary>
    public uint? NetworkMagic { get; }

    /// <summary>Gets the optional encrypted HD derivation path payload from attribute key 1, or <see langword="null"/> when absent.</summary>
    public ReadOnlyMemory<byte>? DerivationPath { get; }

    /// <summary>
    /// Gets whether the address uses the Daedalus-style public-key layout (present HD derivation-path attribute).
    /// This reflects payload layout only and does not prove which wallet software generated the address.
    /// </summary>
    public bool IsDaedalus => AddressType == ByronAddressType.PublicKey && DerivationPath is not null;

    /// <summary>
    /// Gets whether the address uses the Icarus-style public-key layout (absent HD derivation-path attribute).
    /// This reflects payload layout only and does not prove which wallet software generated the address.
    /// </summary>
    public bool IsIcarus => AddressType == ByronAddressType.PublicKey && DerivationPath is null;

    internal ByronAddressInfo(
        ReadOnlyMemory<byte> root,
        ByronAddressType addressType,
        uint crc32,
        uint? networkMagic,
        ReadOnlyMemory<byte>? derivationPath)
    {
        Root = root;
        AddressType = addressType;
        Crc32 = crc32;
        NetworkMagic = networkMagic;
        DerivationPath = derivationPath;
    }

    internal static bool TryDecode(ReadOnlySpan<byte> raw, out ByronAddressSlice slice, out string? error)
    {
        slice = default;
        error = null;
        if (raw.Length < 7 || raw[0] != Address.ByronEnvelopeHeader)
        {
            return Fail("Invalid Byron address CBOR envelope header.", out error);
        }

        try
        {
            CborReader outer = new(raw);
            outer.ReadBeginArray();
            if (outer.ReadSize() != 2)
            {
                return Fail("Byron address envelope must be a 2-element CBOR array.", out error);
            }

            if (!TryPeek(outer.Buffer, out byte head) || (head >> 5) != 6 || !outer.TryReadSemanticTag(out ulong tag) || tag != 24)
            {
                return Fail("Byron address payload must carry CBOR tag 24.", out error);
            }

            if (!TryPeek(outer.Buffer, out head) || (head >> 5) != 2 || (head & 0x1F) == 31)
            {
                return Fail("Byron address payload must be an untagged definite-length byte string.", out error);
            }

            ReadOnlySpan<byte> payload = outer.ReadByteString();
            int payloadOffset = ComputeSliceOffset(0, raw.Length, outer.Buffer.Length, payload.Length);

            if (!TryPeek(outer.Buffer, out head) || (head >> 5) != 0)
            {
                return Fail("Byron address CRC-32 must be an untagged unsigned integer.", out error);
            }

            uint expectedCrc = outer.ReadUInt32();
            if (!outer.Buffer.IsEmpty)
            {
                return Fail("Byron address envelope contains trailing bytes.", out error);
            }

            uint actualCrc = ComputeCrc32(payload);
            if (actualCrc != expectedCrc)
            {
                return Fail("Byron address CRC-32 mismatch.", out error);
            }

            CborReader inner = new(payload);
            if (!TryPeek(inner.Buffer, out head) || (head >> 5) != 4 || (head & 0x1F) == 31)
            {
                return Fail("Byron address payload must be an untagged definite-length CBOR array.", out error);
            }

            inner.ReadBeginArray();
            if (inner.ReadSize() != 3)
            {
                return Fail("Byron address payload must be a 3-element CBOR array.", out error);
            }

            if (!TryPeek(inner.Buffer, out head) || (head >> 5) != 2 || (head & 0x1F) == 31)
            {
                return Fail("Byron address root must be an untagged definite-length byte string.", out error);
            }

            ReadOnlySpan<byte> rootSpan = inner.ReadByteString();
            if (rootSpan.Length != Address.CredentialHashLength)
            {
                return Fail("Byron address root must be exactly 28 bytes.", out error);
            }

            int rootOffset = ComputeSliceOffset(payloadOffset, payload.Length, inner.Buffer.Length, rootSpan.Length);

            if (!TryPeek(inner.Buffer, out head) || (head >> 5) != 5 || (head & 0x1F) == 31)
            {
                return Fail("Byron address attributes must be an untagged definite-length CBOR map.", out error);
            }

            inner.ReadBeginMap();
            int attrCount = inner.ReadSize();
            if (attrCount < 0)
            {
                return Fail("Byron address attributes map must have definite length.", out error);
            }

            bool hasDerivationPath = false;
            int dpOffset = 0;
            int dpLength = 0;
            uint? networkMagic = null;
            int previousKey = -1;

            for (int i = 0; i < attrCount; i++)
            {
                if (!TryPeek(inner.Buffer, out head) || (head >> 5) != 0)
                {
                    return Fail("Byron address attribute key must be an untagged unsigned integer.", out error);
                }

                byte attrKey = inner.ReadByte();
                if (attrKey <= previousKey)
                {
                    return Fail("Byron address attribute keys must be strictly increasing.", out error);
                }

                previousKey = attrKey;

                if (!TryPeek(inner.Buffer, out head) || (head >> 5) != 2 || (head & 0x1F) == 31)
                {
                    return Fail("Byron address attribute value must be an untagged definite-length byte string.", out error);
                }

                ReadOnlySpan<byte> attrVal = inner.ReadByteString();
                int attrValOffset = ComputeSliceOffset(payloadOffset, payload.Length, inner.Buffer.Length, attrVal.Length);

                if (attrKey == 1)
                {
                    CborReader dpReader = new(attrVal);
                    if (!TryPeek(dpReader.Buffer, out byte dpHead) || (dpHead >> 5) != 2 || (dpHead & 0x1F) == 31)
                    {
                        return Fail("Byron HDAddressPayload must be an untagged definite-length byte string.", out error);
                    }

                    ReadOnlySpan<byte> dpSpan = dpReader.ReadByteString();
                    if (!IsMinimalHead(dpHead, (ulong)dpSpan.Length) || !dpReader.Buffer.IsEmpty)
                    {
                        return Fail("Byron HDAddressPayload must use canonical CBOR encoding with no trailing bytes.", out error);
                    }

                    hasDerivationPath = true;
                    dpOffset = ComputeSliceOffset(attrValOffset, attrVal.Length, dpReader.Buffer.Length, dpSpan.Length);
                    dpLength = dpSpan.Length;
                }
                else if (attrKey == 2)
                {
                    CborReader nmReader = new(attrVal);
                    if (!TryPeek(nmReader.Buffer, out byte nmHead) || (nmHead >> 5) != 0)
                    {
                        return Fail("Byron NetworkMagic must be an untagged unsigned integer.", out error);
                    }

                    uint nm = nmReader.ReadUInt32();
                    if (!IsMinimalHead(nmHead, nm) || !nmReader.Buffer.IsEmpty)
                    {
                        return Fail("Byron NetworkMagic must use canonical CBOR encoding with no trailing bytes.", out error);
                    }

                    networkMagic = nm;
                }
            }

            if (!TryPeek(inner.Buffer, out byte typeHead) || (typeHead >> 5) != 0)
            {
                return Fail("Byron address type must be an untagged unsigned integer.", out error);
            }

            uint rawType = inner.ReadUInt32();
            if (!IsMinimalHead(typeHead, rawType))
            {
                return Fail("Byron address type must use canonical CBOR encoding.", out error);
            }

            if (rawType > (uint)ByronAddressType.Redeem)
            {
                return Fail($"Unsupported Byron address type: {rawType}.", out error);
            }

            if (!inner.Buffer.IsEmpty)
            {
                return Fail("Byron address payload contains trailing bytes.", out error);
            }

            slice = new ByronAddressSlice(rootOffset, dpOffset, dpLength, hasDerivationPath, (ByronAddressType)rawType, actualCrc, networkMagic);
            return true;
        }
        catch (CborException ex)
        {
            return Fail($"Invalid Byron address CBOR: {ex.Message}", out error);
        }
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }

    private static bool TryPeek(ReadOnlySpan<byte> buffer, out byte head)
    {
        if (buffer.IsEmpty) { head = 0; return false; }
        head = buffer[0];
        return true;
    }

    private static bool IsMinimalHead(byte head, ulong argument) => (head & 0x1F) switch
    {
        < 24 => true,
        24 => argument >= 24,
        25 => argument > byte.MaxValue,
        26 => argument > ushort.MaxValue,
        27 => argument > uint.MaxValue,
        _ => false
    };

    private static int ComputeSliceOffset(int containerOffset, int containerLength, int remainingLength, int itemLength) =>
        containerOffset + (containerLength - remainingLength - itemLength);

    private static uint[] BuildCrc32Table()
    {
        const uint polynomial = 0xEDB88320u;
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1u) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        uint[] table = Crc32Table;
        foreach (byte b in data)
        {
            crc = (crc >> 8) ^ table[(crc ^ b) & 0xFFu];
        }

        return ~crc;
    }
}

internal readonly record struct ByronAddressSlice(
    int RootOffset,
    int DerivationPathOffset,
    int DerivationPathLength,
    bool HasDerivationPath,
    ByronAddressType AddressType,
    uint Crc32,
    uint? NetworkMagic)
{
    internal ByronAddressInfo Materialize(byte[] owned)
    {
        ReadOnlyMemory<byte>? derivationPath = null;
        if (HasDerivationPath)
        {
            derivationPath = DerivationPathLength == 0
                ? ReadOnlyMemory<byte>.Empty
                : owned.AsMemory(DerivationPathOffset, DerivationPathLength);
        }
        return new ByronAddressInfo(owned.AsMemory(RootOffset, Address.CredentialHashLength), AddressType, Crc32, NetworkMagic, derivationPath);
    }
}
