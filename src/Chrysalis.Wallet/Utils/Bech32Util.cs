using System.Text;

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

    // Bech32 character set index lookup
    private static readonly Dictionary<char, int> CharsetRev = Charset
        .Select((c, i) => new { Character = c, Index = i })
        .ToDictionary(x => x.Character, x => x.Index);

    // Generator coefficients for BCH checksum
    private static readonly uint[] Generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];

    /// <summary>
    /// Encodes a byte array with an HRP into a Bech32 string.
    /// </summary>
    /// <param name="data">The data bytes to encode.</param>
    /// <param name="hrp">The human-readable part prefix.</param>
    /// <returns>The Bech32-encoded string.</returns>
    public static string Encode(byte[] data, string hrp)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(hrp);

        // Check if HRP is valid
        if (string.IsNullOrEmpty(hrp) || hrp.Length < HrpMinLength || hrp.Length > HrpMaxLength || hrp.Any(c => c is < (char)HrpMinValue or > (char)HrpMaxValue))
        {
            throw new ArgumentException("Invalid HRP", nameof(hrp));
        }

        // Convert data to 5-bit values
        byte[] values = ConvertBits(data, 8, 5, true);

        // Create checksum
        byte[] checksum = CreateChecksum(hrp, values);

        // Combine 'values' and 'checksum' into one array
        byte[] combined = new byte[values.Length + checksum.Length];
        Buffer.BlockCopy(values, 0, combined, 0, values.Length);
        Buffer.BlockCopy(checksum, 0, combined, values.Length, checksum.Length);

        // Combine everything and encode
        StringBuilder result = new(hrp.Length + values.Length + checksum.Length + 1);
        _ = result.Append(hrp);
        _ = result.Append(Separator); // Separator

        foreach (byte b in combined)
        {
            _ = result.Append(Charset[b]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Decodes a Bech32 string into an HRP and data parts.
    /// </summary>
    /// <param name="bech32">The Bech32-encoded string to decode.</param>
    /// <returns>A tuple containing the HRP and decoded data bytes.</returns>
    public static (string hrp, byte[] data) Decode(string bech32)
    {
        ArgumentNullException.ThrowIfNull(bech32);

        // Reject mixed-case strings: check that all alphabetic chars are the same case
        bool hasUpper = false;
        bool hasLower = false;
        foreach (char c in bech32)
        {
            if (char.IsUpper(c))
            {
                hasUpper = true;
            }

            if (char.IsLower(c))
            {
                hasLower = true;
            }
        }
        if (hasUpper && hasLower)
        {
            throw new FormatException("Mixed-case strings not allowed");
        }

        // Lowercase for internal operations (convert char by char to avoid CA1308)
        bech32 = string.Create(bech32.Length, bech32, static (span, source) =>
        {
            for (int idx = 0; idx < source.Length; idx++)
            {
                span[idx] = char.ToLowerInvariant(source[idx]);
            }
        });

        // Find the separator
        int pos = bech32.LastIndexOf(Separator);
        if (pos < 1 || pos + 7 > bech32.Length)
        {
            throw new FormatException("Invalid separator position");
        }

        if (bech32[(pos + 1)..].Any(c => !CharsetRev.ContainsKey(c)))
        {
            throw new FormatException("Invalid character in Bech32 string");
        }

        // Extract HRP
        string hrp = bech32[..pos];

        // Extract data part and convert to bytes
        byte[] data = new byte[bech32.Length - pos - 1];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)CharsetRev[bech32[pos + 1 + i]];
        }

        // Verify checksum
        if (!VerifyChecksum(hrp, data))
        {
            throw new FormatException("Invalid Bech32 checksum");
        }

        // Remove the checksum from data (last 6 characters)
        byte[] dataWithoutChecksum = [.. data.Take(data.Length - CheckSumSize)];

        // Convert from 5-bit to 8-bit encoding
        byte[] converted = ConvertBits(dataWithoutChecksum, 5, 8, false);

        return (hrp, converted);
    }

    /// <summary>
    /// Validates if a string is a valid Bech32 encoded value.
    /// </summary>
    /// <param name="bech32">The string to validate.</param>
    /// <returns>True if the string is a valid Bech32 encoding.</returns>
    public static bool Validate(string bech32)
    {
        try
        {
            _ = Decode(bech32);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the Human Readable Part from a Bech32 address.
    /// </summary>
    /// <param name="address">The Bech32-encoded address string.</param>
    /// <returns>The human-readable part prefix.</returns>
    public static string GetHumanReadablePart(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        int pos = address.LastIndexOf(Separator);
        return pos < 1 ? throw new FormatException("Invalid Bech32 string") : address[..pos];
    }

    /// <summary>
    /// Creates a Bech32 checksum.
    /// </summary>
    private static byte[] CreateChecksum(string hrp, byte[] data)
    {
        byte[] hrpExpanded = HrpExpand(hrp);
        byte[] values = [.. hrpExpanded, .. data, .. new byte[6]];

        uint polymod = Polymod(values) ^ 1;
        byte[] checksum = new byte[6];

        for (int i = 0; i < 6; i++)
        {
            checksum[i] = (byte)((polymod >> (5 * (5 - i))) & 31);
        }

        return checksum;
    }

    /// <summary>
    /// Verifies a Bech32 checksum.
    /// </summary>
    private static bool VerifyChecksum(string hrp, byte[] data)
    {
        byte[] hrpExpanded = HrpExpand(hrp);
        byte[] values = [.. hrpExpanded, .. data];

        return Polymod(values) == 1;
    }

    /// <summary>
    /// Expands the HRP for checksum computation.
    /// </summary>
    private static byte[] HrpExpand(string hrp)
    {
        byte[] result = new byte[(hrp.Length * 2) + 1];

        for (int i = 0; i < hrp.Length; i++)
        {
            result[i] = (byte)(hrp[i] >> 5);
            result[i + hrp.Length + 1] = (byte)(hrp[i] & 31);
        }

        result[hrp.Length] = 0;
        return result;
    }

    /// <summary>
    /// Calculates the Bech32 checksum polymod.
    /// </summary>
    private static uint Polymod(byte[] values)
    {
        uint chk = 1;

        foreach (byte value in values)
        {
            uint b = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ value;

            for (int i = 0; i < 5; i++)
            {
                if (((b >> i) & 1) == 1)
                {
                    chk ^= Generator[i];
                }
            }
        }

        return chk;
    }

    /// <summary>
    /// Converts between bit sizes.
    /// </summary>
    /// <param name="data">The input data bytes.</param>
    /// <param name="fromBits">The source bit width.</param>
    /// <param name="toBits">The target bit width.</param>
    /// <param name="pad">Whether to pad the output.</param>
    /// <returns>The converted byte array.</returns>
    public static byte[] ConvertBits(byte[] data, int fromBits, int toBits, bool pad)
    {
        ArgumentNullException.ThrowIfNull(data);

        int acc = 0;
        int bits = 0;
        List<byte> result = [];
        int maxv = (1 << toBits) - 1;

        foreach (byte value in data)
        {
            if ((value >> fromBits) > 0)
            {
                throw new ArgumentException("Invalid input data", nameof(data));
            }

            acc = (acc << fromBits) | value;
            bits += fromBits;

            while (bits >= toBits)
            {
                bits -= toBits;
                result.Add((byte)((acc >> bits) & maxv));
            }
        }

        if (pad)
        {
            if (bits > 0)
            {
                result.Add((byte)((acc << (toBits - bits)) & maxv));
            }
        }
        else if (bits >= fromBits || ((acc << (toBits - bits)) & maxv) != 0)
        {
            throw new ArgumentException("Invalid padding", nameof(data));
        }

        return [.. result];
    }

    /// <summary>
    /// Extracts the payment and delegation parts from a Cardano address byte array.
    /// </summary>
    /// <param name="address">The raw address bytes.</param>
    /// <returns>A tuple containing the payment part and delegation part byte arrays.</returns>
    public static (byte[] paymentPart, byte[] delegationPart) ExtractPaymentAndDelegation(byte[] address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Let's assume the structure of the address:
        // - Prefix (e.g., "addr" or "stake")
        // - Payment Part (usually 28 bytes for the public key hash or script hash)
        // - Delegation Part (usually 28 bytes for the staking credential or key)

        // First, determine the offset of the payment part and delegation part.
        int paymentPartLength = 28;  // Payment part is typically 28 bytes (public key hash)
        int delegationPartLength = 28;  // Delegation part is typically 28 bytes (staking key)

        // Assuming that the address begins with a prefix and the payment part starts right after it
        // Extracting the payment part (next 28 bytes after prefix)
        byte[] paymentPart = new byte[paymentPartLength];
        Array.Copy(address, 1, paymentPart, 0, paymentPartLength);  // Skipping the prefix byte (adjust if needed)

        // Extracting the delegation part (next 28 bytes after the payment part)
        byte[] delegationPart = new byte[delegationPartLength];
        Array.Copy(address, 1 + paymentPartLength, delegationPart, 0, delegationPartLength);  // Skip prefix + payment part

        return (paymentPart, delegationPart);
    }
}
