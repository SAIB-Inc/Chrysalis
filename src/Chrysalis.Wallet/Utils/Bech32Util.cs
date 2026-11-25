// Contains code adapted from CardanoSharp.Wallet (MIT), Copyright (c) 2021 CardanoSharp; changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Runtime.CompilerServices;

namespace Chrysalis.Wallet.Utils;

/// <summary>
/// Utility class for Bech32 encoding and decoding of Cardano addresses.
/// </summary>
public static class Bech32Util
{
    private const int CheckSumSize = 6;
    private const int HrpMinLength = 1;
    private const int HrpMaxLength = 83;
    private const int HrpMinValue = 33;
    private const int HrpMaxValue = 126;
    private const char Separator = '1';
    // Bech32 character set for encoding
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    // Bech32 character set index lookup derived from Charset (single source of truth)
    private static readonly sbyte[] CharsetRev = BuildCharsetRev();

    private readonly ref struct EncodeState(ReadOnlySpan<byte> data, ReadOnlySpan<char> hrp)
    {
        public ReadOnlySpan<byte> Data { get; } = data;
        public ReadOnlySpan<char> Hrp { get; } = hrp;
    }

    /// <summary>
    /// Encodes a byte array with an HRP into a Bech32 string.
    /// </summary>
    /// <param name="data">The data bytes to encode.</param>
    /// <param name="hrp">The human-readable part prefix.</param>
    /// <returns>The Bech32-encoded string.</returns>
    public static string Encode(byte[] data, string hrp)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Encode(data.AsSpan(), hrp);
    }

    /// <summary>
    /// Encodes a byte span with an HRP into a Bech32 string.
    /// </summary>
    /// <param name="data">The data bytes to encode.</param>
    /// <param name="hrp">The human-readable part prefix.</param>
    /// <returns>The Bech32-encoded string.</returns>
    public static string Encode(ReadOnlySpan<byte> data, string hrp)
    {
        ArgumentNullException.ThrowIfNull(hrp);

        if (hrp.Length is < HrpMinLength or > HrpMaxLength)
        {
            throw new ArgumentException("Invalid HRP", nameof(hrp));
        }

        for (int i = 0; i < hrp.Length; i++)
        {
            char c = hrp[i];
            if (c is < (char)HrpMinValue or > (char)HrpMaxValue)
            {
                throw new ArgumentException("Invalid HRP", nameof(hrp));
            }
        }

        int dataCharCount = checked((int)((((long)data.Length * 8) + 4) / 5));
        int totalLength = checked(hrp.Length + 1 + dataCharCount + CheckSumSize);
        return string.Create(totalLength, new EncodeState(data, hrp), static (dst, state) => EncodeCore(state.Data, state.Hrp, dst));
    }

    /// <summary>
    /// Decodes a Bech32 string into an HRP and data parts.
    /// </summary>
    /// <param name="bech32">The Bech32-encoded string to decode.</param>
    /// <returns>A tuple containing the HRP and decoded data bytes.</returns>
    public static (string hrp, byte[] data) Decode(string bech32)
    {
        ArgumentNullException.ThrowIfNull(bech32);

        if (!TryValidateBech32(bech32, out int separatorIndex, out bool hasUpper, out string? error))
        {
            throw new FormatException(error);
        }

        int dataCharCount = bech32.Length - separatorIndex - 1 - CheckSumSize;
        int byteCount = checked((int)(5L * dataCharCount / 8));
        byte[] decoded = byteCount == 0 ? [] : new byte[byteCount];
        if (!TryDecodeDataPart(bech32.AsSpan(separatorIndex + 1, dataCharCount), decoded, writeOutput: true))
        {
            throw new FormatException("Invalid Bech32 padding");
        }

        string hrp = hasUpper
            ? string.Create(separatorIndex, bech32, static (dst, src) =>
            {
                for (int i = 0; i < dst.Length; i++)
                {
                    dst[i] = ToLowerAscii(src[i]);
                }
            })
            : bech32[..separatorIndex];

        return (hrp, decoded);
    }

    /// <summary>
    /// Validates if a string is a valid Bech32 encoded value.
    /// </summary>
    /// <param name="bech32">The string to validate.</param>
    /// <returns>True if the string is a valid Bech32 encoding.</returns>
    public static bool Validate(string bech32)
    {
        if (bech32 is null)
        {
            return false;
        }

        if (!TryValidateBech32(bech32, out int separatorIndex, out _, out _))
        {
            return false;
        }

        int dataCharCount = bech32.Length - separatorIndex - 1 - CheckSumSize;
        return TryDecodeDataPart(bech32.AsSpan(separatorIndex + 1, dataCharCount), [], writeOutput: false);
    }

    /// <summary>
    /// Extracts the Human Readable Part from a Bech32 address.
    /// </summary>
    /// <param name="address">The Bech32-encoded address string.</param>
    /// <returns>The human-readable part prefix.</returns>
    public static string GetHumanReadablePart(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        int pos = address.LastIndexOf(Separator, StringComparison.Ordinal);
        return pos < 1 ? throw new FormatException("Invalid Bech32 string") : address[..pos];
    }

    private static sbyte[] BuildCharsetRev()
    {
        sbyte[] table = new sbyte[128];
        Array.Fill(table, (sbyte)-1);
        for (int i = 0; i < Charset.Length; i++)
        {
            char c = Charset[i];
            table[c] = (sbyte)i;
            if (c is >= 'a' and <= 'z')
            {
                table[c - 32] = (sbyte)i;
            }
        }

        return table;
    }

    private static void EncodeCore(ReadOnlySpan<byte> data, ReadOnlySpan<char> hrp, Span<char> destination)
    {
        uint chk = 1;
        for (int i = 0; i < hrp.Length; i++)
        {
            char lower = ToLowerAscii(hrp[i]);
            destination[i] = lower;
            chk = PolymodStep(chk, lower >> 5);
        }

        chk = PolymodStep(chk, 0);
        for (int i = 0; i < hrp.Length; i++)
        {
            chk = PolymodStep(chk, destination[i] & 31);
        }

        int pos = hrp.Length;
        destination[pos++] = Separator;

        int acc = 0;
        int bits = 0;
        for (int i = 0; i < data.Length; i++)
        {
            acc = (acc << 8) | data[i];
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                int value = (acc >> bits) & 31;
                destination[pos++] = Charset[value];
                chk = PolymodStep(chk, value);
            }
        }

        if (bits > 0)
        {
            int value = (acc << (5 - bits)) & 31;
            destination[pos++] = Charset[value];
            chk = PolymodStep(chk, value);
        }

        for (int i = 0; i < CheckSumSize; i++)
        {
            chk = PolymodStep(chk, 0);
        }

        chk ^= 1;
        for (int i = 0; i < CheckSumSize; i++)
        {
            destination[pos++] = Charset[(int)((chk >> (5 * (5 - i))) & 31)];
        }
    }

    private static bool TryValidateBech32(ReadOnlySpan<char> bech32, out int separatorIndex, out bool hasUpper, out string? error)
    {
        hasUpper = false;
        separatorIndex = bech32.LastIndexOf(Separator);
        if (separatorIndex < HrpMinLength || separatorIndex > HrpMaxLength || separatorIndex + 1 + CheckSumSize > bech32.Length)
        {
            error = "Invalid separator position";
            return false;
        }

        bool hasLower = false;
        uint chk = 1;
        for (int i = 0; i < separatorIndex; i++)
        {
            char c = bech32[i];
            if (c is < (char)HrpMinValue or > (char)HrpMaxValue)
            {
                error = "Invalid HRP";
                return false;
            }

            if (c is >= 'A' and <= 'Z')
            {
                hasUpper = true;
                c = (char)(c | 0x20);
            }
            else if (c is >= 'a' and <= 'z')
            {
                hasLower = true;
            }

            chk = PolymodStep(chk, c >> 5);
        }

        chk = PolymodStep(chk, 0);
        for (int i = 0; i < separatorIndex; i++)
        {
            chk = PolymodStep(chk, ToLowerAscii(bech32[i]) & 31);
        }

        sbyte[] rev = CharsetRev;
        for (int i = separatorIndex + 1; i < bech32.Length; i++)
        {
            char c = bech32[i];
            int val = c < 128 ? rev[c] : -1;
            if (val < 0)
            {
                error = "Invalid character in Bech32 string";
                return false;
            }

            if (c is >= 'A' and <= 'Z')
            {
                hasUpper = true;
            }
            else if (c is >= 'a' and <= 'z')
            {
                hasLower = true;
            }

            chk = PolymodStep(chk, val);
        }

        if (hasUpper && hasLower)
        {
            error = "Mixed-case strings not allowed";
            return false;
        }

        if (chk != 1)
        {
            error = "Invalid Bech32 checksum";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryDecodeDataPart(ReadOnlySpan<char> dataChars, Span<byte> destination, bool writeOutput)
    {
        sbyte[] rev = CharsetRev;
        int acc = 0;
        int bits = 0;
        int pos = 0;
        for (int i = 0; i < dataChars.Length; i++)
        {
            acc = (acc << 5) | (byte)rev[dataChars[i]];
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                if (writeOutput)
                {
                    destination[pos++] = (byte)(acc >> bits);
                }
            }
        }

        return bits < 5 && (acc & ((1 << bits) - 1)) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PolymodStep(uint chk, int value)
    {
        uint top = chk >> 25;
        chk = ((chk & 0x1ffffff) << 5) ^ (uint)value;
        if ((top & 1) != 0) { chk ^= 0x3b6a57b2; }
        if ((top & 2) != 0) { chk ^= 0x26508e6d; }
        if ((top & 4) != 0) { chk ^= 0x1ea119fa; }
        if ((top & 8) != 0) { chk ^= 0x3d4233dd; }
        if ((top & 16) != 0) { chk ^= 0x2a1462b3; }
        return chk;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char ToLowerAscii(char c) => c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c;
}
