using System.Text;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.Test.Utils;

// Test-only vectors: BIP-0173 general valid (:277-283), general invalid (:291-302), and invalid-padding (:323)
// literals from https://github.com/bitcoin/bips at b9f9a8d6e854fa0b0c8f818753420b0aa6e875aa, bip-0173.mediawiki
// (sha256 42cbf2c778e14dd7be348823c9d2f4ef9682da4b3f5a986533bfd270601a0982; Authors: Pieter Wuille, Greg Maxwell;
// BSD-2-Clause; see NOTICE-bip173 and THIRD-PARTY-NOTICES.md). All generated payloads below are deterministic
// test-only data (never real keys or recovery phrases).
public class Bech32UtilTests
{
    private const string ReferenceCharset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private const string ZeroBytes51Hex = "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    private static readonly uint[] ReferenceGenerator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
    private static readonly string[] TestOnlyCardanoHrps =
    [
        "addr", "addr_test", "stake", "stake_test",
        "addr_vk", "addr_xvk", "stake_vk", "stake_xvk",
        "script", "drep", "pool",
    ];
    private static readonly byte[] Cip19HeaderBytes =
    [
        0x00, 0x01, 0x10, 0x11, 0x20, 0x21, 0x30, 0x31, 0x40, 0x41,
        0x50, 0x51, 0x60, 0x61, 0x70, 0x71, 0xe0, 0xe1, 0xf0, 0xf1,
    ];
    private static readonly int[] FixedPayloadLengths = [0, 1, 2, 3, 4, 5, 28, 29, 57, 512, 4096];
    private static readonly string[] InvalidHrps = ["", new string('a', 84), " ", "a\u0020b", "a\u007Fb", "a\u0080b", "\u007F"];

    [Theory]
    [InlineData("A12UEL5L", "a", "", "a12uel5l")]
    [InlineData("a12uel5l", "a", "", "a12uel5l")]
    [InlineData(
        "an83characterlonghumanreadablepartthatcontainsthenumber1andtheexcludedcharactersbio1tt5tgs",
        "an83characterlonghumanreadablepartthatcontainsthenumber1andtheexcludedcharactersbio",
        "",
        "an83characterlonghumanreadablepartthatcontainsthenumber1andtheexcludedcharactersbio1tt5tgs")]
    [InlineData(
        "abcdef1qpzry9x8gf2tvdw0s3jn54khce6mua7lmqqqxw",
        "abcdef",
        "00443214c74254b635cf84653a56d7c675be77df",
        "abcdef1qpzry9x8gf2tvdw0s3jn54khce6mua7lmqqqxw")]
    [InlineData(
        "11qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqc8247j",
        "1",
        ZeroBytes51Hex,
        "11qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqc8247j")]
    [InlineData(
        "split1checkupstagehandshakeupstreamerranterredcaperred2y9e3w",
        "split",
        "c5f38b70305f519bf66d85fb6cf03058f3dde463ecd7918f2dc743918f2d",
        "split1checkupstagehandshakeupstreamerranterredcaperred2y9e3w")]
    [InlineData("?1ezyfcl", "?", "", "?1ezyfcl")]
    public void Decode_Bip173ValidVectors_MatchesExpectedAndRoundTrips(
        string encoded,
        string expectedHrp,
        string expectedDataHex,
        string expectedCanonical)
    {
        ArgumentNullException.ThrowIfNull(expectedCanonical);
        byte[] expectedBytes = Convert.FromHexString(expectedDataHex);

        Assert.True(Bech32Util.Validate(encoded));
        (string hrp, byte[] data) = Bech32Util.Decode(encoded);
        Assert.Equal(expectedHrp, hrp);
        Assert.Equal(expectedBytes, data);

        Assert.Equal(expectedCanonical, Bech32Util.Encode(data, hrp));
        Assert.Equal(expectedCanonical, Bech32Util.Encode(data.AsSpan(), hrp));

        string upper = expectedCanonical.ToUpperInvariant();
        Assert.True(Bech32Util.Validate(upper));
        (string upperHrp, byte[] upperData) = Bech32Util.Decode(upper);
        Assert.Equal(expectedHrp, upperHrp);
        Assert.Equal(expectedBytes, upperData);
    }

    [Theory]
    [InlineData("\u00201nwldj5")]
    [InlineData("\u007F1axkwrx")]
    [InlineData("\u00801eym55h")]
    [InlineData("an84characterslonghumanreadablepartthatcontainsthenumber1andtheexcludedcharactersbio1569pvx")]
    [InlineData("pzry9x0s0muk")]
    [InlineData("1pzry9x0s0muk")]
    [InlineData("x1b4n0q5v")]
    [InlineData("li1dgmt3")]
    [InlineData("de1lg7wt\u00FF")]
    [InlineData("A1G7SGD8")]
    [InlineData("10a06t8")]
    [InlineData("1qzzfhee")]
    [InlineData("")]
    [InlineData("Addr1vyqqae8987")]
    [InlineData("addr1vyQqae8987")]
    public void Decode_Bip173InvalidStrings_ThrowsFormatExceptionAndValidateReturnsFalse(string invalid)
    {
        Assert.False(Bech32Util.Validate(invalid));
        _ = Assert.Throws<FormatException>(() => Bech32Util.Decode(invalid));
    }

    [Theory]
    [InlineData("a1q3g6mn3")]
    [InlineData("a1qpamnt9j")]
    [InlineData("a1qqqd87cpp")]
    [InlineData("tb1qrp33g0q5c5txsp9arysrx4k6zdkfs4nce4xj0gdcccefvpysxf3pjxtptv")]
    public void Decode_InvalidPadding_ThrowsFormatExceptionAndValidateReturnsFalse(string invalid)
    {
        Assert.False(Bech32Util.Validate(invalid));
        _ = Assert.Throws<FormatException>(() => Bech32Util.Decode(invalid));
    }

    [Theory]
    [InlineData("A", "", "a12uel5l")]
    [InlineData("ADDR", "6100", "addr1vyqqae8987")]
    public void Encode_UppercaseHrp_EmitsBip173LowercaseAndRoundTrips(string hrp, string payloadHex, string expected)
    {
        ArgumentNullException.ThrowIfNull(hrp);
        ArgumentNullException.ThrowIfNull(expected);
        byte[] payload = Convert.FromHexString(payloadHex);

        string fromArray = Bech32Util.Encode(payload, hrp);
        string fromSpan = Bech32Util.Encode(payload.AsSpan(), hrp);

        Assert.Equal(expected, fromArray);
        Assert.Equal(expected, fromSpan);
        Assert.True(Bech32Util.Validate(fromArray));
        (string decodedHrp, byte[] decodedData) = Bech32Util.Decode(fromArray);
        Assert.Equal(expected[..hrp.Length], decodedHrp);
        Assert.Equal(payload, decodedData);
    }

    [Fact]
    public void EncodeAndDecode_Differential_MatchesReferenceAcrossCardanoKindsAndLongInput()
    {
        // Deterministic test-only PRNG seeded with 20261125 (avoids CA5394 on System.Random).
        uint state = 20261125u;
        for (int i = 0; i < 100; i++)
        {
            string hrp = TestOnlyCardanoHrps[i % TestOnlyCardanoHrps.Length];
            int length = i < FixedPayloadLengths.Length
                ? FixedPayloadLengths[i]
                : (int)(NextUInt(ref state) % 129u);
            byte[] payload = new byte[length];
            for (int j = 0; j < payload.Length; j++)
            {
                payload[j] = (byte)NextUInt(ref state);
            }

            if (payload.Length > 0 && i < Cip19HeaderBytes.Length)
            {
                payload[0] = Cip19HeaderBytes[i];
            }

            string expected = ReferenceEncode(payload, hrp);
            string actualArray = Bech32Util.Encode(payload, hrp);
            string actualSpan = Bech32Util.Encode(payload.AsSpan(), hrp);
            Assert.Equal(expected, actualArray);
            Assert.Equal(expected, actualSpan);

            Assert.True(Bech32Util.Validate(actualArray));
            (string decodedHrp, byte[] decodedData) = Bech32Util.Decode(actualArray);
            Assert.Equal(hrp, decodedHrp);
            Assert.Equal(payload, decodedData);

            string upper = actualArray.ToUpperInvariant();
            Assert.True(Bech32Util.Validate(upper));
            (string upperHrp, byte[] upperData) = Bech32Util.Decode(upper);
            Assert.Equal(hrp, upperHrp);
            Assert.Equal(payload, upperData);
        }
    }

    [Fact]
    public void Decode_CharsetExhaustive_AcceptsAll55ValidAsciiAndRejectsAll73InvalidAscii()
    {
        string[] lowerSamples = new string[32];
        string[] upperSamples = new string[32];
        bool[] isValidAscii = new bool[128];
        int validCount = 0;

        for (int v = 0; v < 32; v++)
        {
            byte[] sample = [(byte)(v << 3), 0, 0, 0, 0];
            string lower = ReferenceEncode(sample, "a");
            string upper = lower.ToUpperInvariant();
            lowerSamples[v] = lower;
            upperSamples[v] = upper;

            char c = ReferenceCharset[v];
            isValidAscii[c] = true;
            validCount++;
            Assert.True(Bech32Util.Validate(lower));
            (string lowerHrp, byte[] lowerData) = Bech32Util.Decode(lower);
            Assert.Equal("a", lowerHrp);
            Assert.Equal(sample, lowerData);

            if (c is >= 'a' and <= 'z')
            {
                isValidAscii[c - 32] = true;
                validCount++;
                Assert.True(Bech32Util.Validate(upper));
                (string upperHrp, byte[] upperData) = Bech32Util.Decode(upper);
                Assert.Equal("a", upperHrp);
                Assert.Equal(sample, upperData);
            }
        }

        Assert.Equal(55, validCount);

        int invalidCount = 0;
        for (int code = 0; code < 128; code++)
        {
            char c = (char)code;
            if (isValidAscii[code] || c == '1')
            {
                continue;
            }

            invalidCount++;
            string[] samples = c is >= 'A' and <= 'Z' ? upperSamples : lowerSamples;
            ReadOnlySpan<char> ch = [c];
            for (int v = 0; v < 32; v++)
            {
                string mutated = string.Concat(samples[v].AsSpan(0, 2), ch, samples[v].AsSpan(3));
                Assert.False(Bech32Util.Validate(mutated));
                _ = Assert.Throws<FormatException>(() => Bech32Util.Decode(mutated));
            }
        }

        Assert.Equal(72, invalidCount);

        string oneInData = string.Concat(lowerSamples[0].AsSpan(0, 2), "1", lowerSamples[0].AsSpan(3));
        string oneInChecksum = string.Concat(lowerSamples[0].AsSpan(0, lowerSamples[0].Length - 1), "1");
        Assert.False(Bech32Util.Validate(oneInData));
        _ = Assert.Throws<FormatException>(() => Bech32Util.Decode(oneInData));
        Assert.False(Bech32Util.Validate(oneInChecksum));
        _ = Assert.Throws<FormatException>(() => Bech32Util.Decode(oneInChecksum));
    }

    [Fact]
    public void Encode_InvalidHrpAndNulls_ThrowExpectedExceptions()
    {
        byte[] empty = [];
        _ = Assert.Throws<ArgumentNullException>("data", () => Bech32Util.Encode(null!, "addr"));
        _ = Assert.Throws<ArgumentNullException>("hrp", () => Bech32Util.Encode(empty, null!));
        _ = Assert.Throws<ArgumentNullException>("hrp", () => Bech32Util.Encode([], null!));

        foreach (string badHrp in InvalidHrps)
        {
            _ = Assert.Throws<ArgumentException>("hrp", () => Bech32Util.Encode(empty, badHrp));
            _ = Assert.Throws<ArgumentException>("hrp", () => Bech32Util.Encode([], badHrp));
        }

        _ = Assert.Throws<ArgumentNullException>("bech32", () => Bech32Util.Decode(null!));
        Assert.False(Bech32Util.Validate(null!));
    }

    [Fact]
    public void EncodeAndDecode_InParallel_MatchesSequentialWithNoSharedScratch()
    {
        byte[] longPayload = new byte[4096];
        for (int i = 0; i < longPayload.Length; i++)
        {
            longPayload[i] = (byte)((i * 37) ^ 0x5A);
        }

        (string Hrp, byte[] Payload)[] cases =
        [
            ("addr", Convert.FromHexString("01" + new string('1', 56) + new string('2', 56))),
            ("addr_test", Convert.FromHexString("00" + new string('3', 56) + new string('4', 56))),
            ("stake", Convert.FromHexString("e1" + new string('5', 56))),
            ("a", []),
            ("addr_test", longPayload),
        ];

        string[] expectedEncoded = [.. cases.Select(c => ReferenceEncode(c.Payload, c.Hrp))];
        string[] expectedUpper = [.. expectedEncoded.Select(s => s.ToUpperInvariant())];

        _ = Parallel.For(0, 64, i =>
        {
            int idx = i % cases.Length;
            (string hrp, byte[] payload) = cases[idx];
            Assert.Equal(expectedEncoded[idx], Bech32Util.Encode(payload, hrp));
            string encoded = Bech32Util.Encode(payload.AsSpan(), hrp);
            Assert.Equal(expectedEncoded[idx], encoded);
            Assert.True(Bech32Util.Validate(encoded));
            (string decHrp, byte[] decData) = Bech32Util.Decode(encoded);
            Assert.Equal(hrp, decHrp);
            Assert.Equal(payload, decData);

            Assert.True(Bech32Util.Validate(expectedUpper[idx]));
            (string upHrp, byte[] upData) = Bech32Util.Decode(expectedUpper[idx]);
            Assert.Equal(hrp, upHrp);
            Assert.Equal(payload, upData);
        });
    }

    private static uint NextUInt(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static string ReferenceEncode(byte[] data, string hrp)
    {
        List<int> quintets = [];
        int acc = 0;
        int bits = 0;
        foreach (byte b in data)
        {
            acc = (acc << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                quintets.Add((acc >> bits) & 31);
            }
        }

        if (bits > 0)
        {
            quintets.Add((acc << (5 - bits)) & 31);
        }

        uint chk = 1;
        foreach (char c in hrp)
        {
            chk = ReferencePolymodStep(chk, c >> 5);
        }

        chk = ReferencePolymodStep(chk, 0);
        foreach (char c in hrp)
        {
            chk = ReferencePolymodStep(chk, c & 31);
        }

        foreach (int q in quintets)
        {
            chk = ReferencePolymodStep(chk, q);
        }

        for (int i = 0; i < 6; i++)
        {
            chk = ReferencePolymodStep(chk, 0);
        }

        chk ^= 1;

        StringBuilder sb = new(hrp.Length + 1 + quintets.Count + 6);
        _ = sb.Append(hrp).Append('1');
        foreach (int q in quintets)
        {
            _ = sb.Append(ReferenceCharset[q]);
        }

        for (int i = 0; i < 6; i++)
        {
            _ = sb.Append(ReferenceCharset[(int)((chk >> (5 * (5 - i))) & 31)]);
        }

        return sb.ToString();
    }

    private static uint ReferencePolymodStep(uint chk, int value)
    {
        uint top = chk >> 25;
        chk = ((chk & 0x1ffffff) << 5) ^ (uint)value;
        for (int i = 0; i < 5; i++)
        {
            if (((top >> i) & 1) != 0)
            {
                chk ^= ReferenceGenerator[i];
            }
        }

        return chk;
    }
}
