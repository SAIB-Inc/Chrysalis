using System.Buffers;
using System.Buffers.Text;
using System.Text;
using Chrysalis.Codec.Serialization;
using Chrysalis.Wallet.CIPs.CIP8.Models;
using Chrysalis.Wallet.Utils;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Wallet.CIPs.CIP8.Extensions;

/// <summary>
/// Extension methods for COSE message handling and CIP-8 format conversion.
/// </summary>
public static class CoseMessageExtensions
{
    private const string Cip8Prefix = "cms_";
    private const int ChecksumByteLength = sizeof(uint);
    private static readonly int EncodedChecksumLength = Base64Url.GetEncodedLength(ChecksumByteLength);
    private static readonly SearchValues<char> Base64UrlChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    /// <summary>
    /// Converts a COSE message to CIP-8 format with prefix and checksum.
    /// </summary>
    /// <param name="message">The COSE message to convert.</param>
    /// <returns>The CIP-8 formatted string representation.</returns>
    public static string ToCip8Format(this ICoseMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        string prefix = message switch
        {
            CoseSign1 => Cip8Prefix,
            _ => throw new NotSupportedException($"Message type {message.GetType().Name} is not supported for CIP-8 format")
        };

        byte[] cbor = message.ToCbor();
        int totalLength = prefix.Length + Base64Url.GetEncodedLength(cbor.Length) + 1 + EncodedChecksumLength;
        return string.Create(totalLength, (prefix, cbor), static (span, state) =>
        {
            state.prefix.AsSpan().CopyTo(span);
            int separatorIndex = state.prefix.Length + Base64Url.EncodeToChars(state.cbor, span[state.prefix.Length..]);
            Span<byte> checksumBytes = stackalloc byte[ChecksumByteLength];
            _ = BitConverter.TryWriteBytes(checksumBytes, CalculateFnv32a(span[..separatorIndex]));
            span[separatorIndex] = '_';
            _ = Base64Url.EncodeToChars(checksumBytes, span[(separatorIndex + 1)..]);
        });
    }

    /// <summary>
    /// Parses a CIP-8 formatted message.
    /// </summary>
    /// <param name="cip8Message">The CIP-8 formatted string to parse.</param>
    /// <returns>A deserialized CoseSign1 message.</returns>
    public static CoseSign1 FromCip8Format(string cip8Message)
    {
        ArgumentNullException.ThrowIfNull(cip8Message);
        ReadOnlySpan<char> span = cip8Message.AsSpan();
        if (!Ascii.IsValid(span))
        {
            throw new FormatException("Invalid CIP-8 format. Message must contain ASCII characters only.");
        }

        int firstUnderscore = span.IndexOf('_');
        int separatorIndex = span.Length - EncodedChecksumLength - 1;
        if (firstUnderscore == -1 || separatorIndex <= firstUnderscore + 1 || span[separatorIndex] != '_')
        {
            throw new FormatException("Invalid CIP-8 format. Expected format: prefix_data_checksum");
        }

        ReadOnlySpan<char> prefix = span[..(firstUnderscore + 1)];
        ReadOnlySpan<char> data = span[(firstUnderscore + 1)..separatorIndex];
        ReadOnlySpan<char> checksum = span[(separatorIndex + 1)..];
        if (!TryValidateCanonicalBase64Url(data, out int decodedLength) ||
            !TryValidateCanonicalBase64Url(checksum, out int checksumDecodedLength) ||
            checksumDecodedLength != ChecksumByteLength)
        {
            throw new FormatException("Invalid CIP-8 format. Expected canonical unpadded Base64Url characters.");
        }

        Span<byte> expectedChecksum = stackalloc byte[ChecksumByteLength];
        _ = BitConverter.TryWriteBytes(expectedChecksum, CalculateFnv32a(span[..separatorIndex]));
        Span<byte> actualChecksum = stackalloc byte[ChecksumByteLength];
        if (!Base64Url.TryDecodeFromChars(checksum, actualChecksum, out int actualChecksumLength) ||
            actualChecksumLength != ChecksumByteLength ||
            !expectedChecksum.SequenceEqual(actualChecksum))
        {
            throw new InvalidOperationException("Invalid checksum in CIP-8 message");
        }

        if (!prefix.SequenceEqual(Cip8Prefix))
        {
            throw new NotSupportedException($"Unknown CIP-8 prefix: {prefix}");
        }

        byte[] cbor = new byte[decodedLength];
        if (!Base64Url.TryDecodeFromChars(data, cbor, out int written) || written != decodedLength)
        {
            throw new FormatException("Invalid CIP-8 format. Invalid Base64Url payload.");
        }

        return CborSerializer.Deserialize<CoseSign1>(cbor);
    }

    /// <summary>
    /// Reconstructs the SigStructure for verification, honoring the CIP-8 unprotected <c>"hashed"</c> header.
    /// </summary>
    /// <param name="message">The CoseSign1 message to reconstruct the SigStructure for.</param>
    /// <param name="externalAad">Optional external additional authenticated data.</param>
    /// <param name="payload">Optional raw plaintext payload (never pre-hashed). It is hashed with Blake2b-224 when the unprotected <c>"hashed"</c> header is <see langword="true"/> and is always checked against any attached payload, whether hashed or not. A missing <c>"hashed"</c> header is treated as <see langword="false"/>; <c>"version"</c> is neither required nor emitted.</param>
    /// <remarks>This method does not verify the signature. With <paramref name="payload"/> set to <see langword="null"/>, subsequent successful signature verification authenticates only that the signing key signed the attached bytes and protected headers verbatim; it cannot distinguish a raw 28-byte attachment from a digest or prove key-to-address ownership.</remarks>
    /// <returns>The reconstructed SigStructure.</returns>
    /// <exception cref="ArgumentException">Both <paramref name="payload"/> and <see cref="CoseSign1.Payload"/> are <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The unprotected <c>"hashed"</c> header is malformed, non-boolean, or duplicated; an attached hashed payload is not 28 bytes; or <paramref name="payload"/> does not match the attached payload.</exception>
    public static SigStructure GetSigStructure(this CoseSign1 message, byte[]? externalAad = null, byte[]? payload = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        bool isHashed = message.IsPayloadHashed();
        byte[] actualPayload;
        if (payload is not null)
        {
            actualPayload = isHashed ? HashUtil.Blake2b224(payload) : payload;
            if (message.Payload is not null && !message.Payload.AsSpan().SequenceEqual(actualPayload))
            {
                throw new FormatException("Provided payload does not match the attached COSE_Sign1 payload.");
            }
        }
        else if (message.Payload is not null)
        {
            if (isHashed && message.Payload.Length != HashUtil.Blake2b224Size)
            {
                throw new FormatException($"A CIP-8 hashed COSE_Sign1 payload must be {HashUtil.Blake2b224Size} bytes, not {message.Payload.Length}.");
            }
            actualPayload = message.Payload;
        }
        else
        {
            throw new ArgumentException("Payload is required. Either the message must contain it or it must be provided as a parameter.", nameof(payload));
        }

        return new SigStructure(SigContext.Signature1, message.ProtectedHeaders, [], externalAad ?? [], actualPayload);
    }

    internal static bool IsPayloadHashed(this CoseSign1 message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ReadOnlyMemory<byte> raw = message.UnprotectedHeaders.Raw;
        if (raw.IsEmpty)
        {
            return false;
        }

        try
        {
            CborReader reader = new(raw.Span);
            if ((reader.Buffer[0] >> 5) != 5)
            {
                throw new FormatException("CIP-8 unprotected headers must be a CBOR map.");
            }
            reader.ReadBeginMap();
            int remaining = reader.ReadSize();
            bool? hashed = null;
            while (remaining >= 0 ? remaining > 0 : !reader.Buffer.IsEmpty && reader.Buffer[0] != 0xFF)
            {
                bool isHashedKey = false;
                if (reader.Buffer.IsEmpty || reader.Buffer[0] == 0xFF)
                {
                    throw new FormatException("Truncated CBOR map in CIP-8 unprotected headers.");
                }
                if ((reader.Buffer[0] >> 5) == 3)
                {
                    isHashedKey = string.Equals(reader.ReadString(), HeaderLabels.Hashed, StringComparison.Ordinal);
                }
                else
                {
                    reader.SkipDataItem();
                }
                if (reader.Buffer.IsEmpty || reader.Buffer[0] == 0xFF)
                {
                    throw new FormatException("Truncated CBOR map in CIP-8 unprotected headers.");
                }
                if (isHashedKey)
                {
                    if (hashed.HasValue)
                    {
                        throw new FormatException("Duplicate CIP-8 'hashed' unprotected header.");
                    }
                    if (reader.Buffer[0] is not (0xF4 or 0xF5))
                    {
                        throw new FormatException("CIP-8 'hashed' unprotected header must be a boolean.");
                    }
                    hashed = reader.ReadBoolean();
                }
                else
                {
                    reader.SkipDataItem();
                }
                remaining -= remaining > 0 ? 1 : 0;
            }
            bool validEnd = remaining < 0 ? reader.Buffer.Length == 1 && reader.Buffer[0] == 0xFF : reader.Buffer.IsEmpty;
            if (!validEnd)
            {
                throw new FormatException("Unterminated or trailing CBOR in CIP-8 unprotected headers.");
            }
            return hashed ?? false;
        }
        catch (CborException ex)
        {
            throw new FormatException("Malformed CBOR in CIP-8 unprotected headers.", ex);
        }
    }

    private static bool TryValidateCanonicalBase64Url(ReadOnlySpan<char> text, out int decodedLength)
    {
        if (text.ContainsAnyExcept(Base64UrlChars))
        {
            decodedLength = 0;
            return false;
        }
        return Base64Url.IsValid(text, out decodedLength);
    }

    private static uint CalculateFnv32a(ReadOnlySpan<char> data)
    {
        uint hash = 0x811c9dc5;
        foreach (char c in data)
        {
            hash ^= (byte)c;
            hash *= 0x01000193;
        }
        return hash;
    }
}
