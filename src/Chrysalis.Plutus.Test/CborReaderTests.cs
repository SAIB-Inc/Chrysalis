using System.Numerics;
using Chrysalis.Plutus.Cbor;
using Chrysalis.Plutus.Types;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed class CborReaderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("d8")]
    [InlineData("1a0001")]
    [InlineData("5805aabb")]
    [InlineData("9bffffffffffffffff")]
    [InlineData("9f01")]
    [InlineData("a101")]
    [InlineData("5f01ff")]
    [InlineData("c201")]
    [InlineData("d866822080")]
    [InlineData("d879a0")]
    [InlineData("d8795fff")]
    [InlineData("d86683008000")]
    [InlineData("d8669f008000ff")]
    public void CborReaderRefusesWhatTheNodeRefuses(string hex) =>
        _ = Assert.Throws<InvalidOperationException>(() => CborReader.DecodePlutusData(Convert.FromHexString(hex)));

    public static TheoryData<string, byte[], PlutusData> ValidCases()
    {
        byte[] sixtyFour = [.. Enumerable.Range(0, 64).Select(i => (byte)i)];
        byte[] definite64 = [0x58, 0x40, .. sixtyFour];
        BigInteger pow520Minus1 = (BigInteger.One << 520) - 1;
        return new()
        {
            { "tag 102 indefinite outer", Convert.FromHexString("d8669f0080ff"), new PlutusDataConstr(0, []) },
            { "chunked small bignum", Convert.FromHexString("c25f41014102ff"), new PlutusDataInteger(258) },
            { "64-byte definite byte string", definite64, new PlutusDataByteString(sixtyFour) },
            { "65-byte chunked bignum", Convert.FromHexString("c25f5840" + new string('f', 128) + "41ffff"), new PlutusDataInteger(pow520Minus1) },
            { "writer round-trip 65-byte bignum", CborWriter.EncodePlutusData(new PlutusDataInteger(pow520Minus1)), new PlutusDataInteger(pow520Minus1) }
        };
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void CborReaderReadsWhatTheNodeReads(string name, byte[] input, PlutusData expected)
    {
        _ = name;
        ArgumentNullException.ThrowIfNull(expected);
        PlutusData actual = CborReader.DecodePlutusData(input);
        switch (expected)
        {
            case PlutusDataConstr ec:
                PlutusDataConstr ac = Assert.IsType<PlutusDataConstr>(actual);
                Assert.Equal(ec.Tag, ac.Tag);
                Assert.Equal(ec.Fields, ac.Fields);
                break;
            case PlutusDataInteger ei:
                Assert.Equal(ei.Value, Assert.IsType<PlutusDataInteger>(actual).Value);
                break;
            case PlutusDataByteString eb:
                Assert.Equal(eb.Value.ToArray(), Assert.IsType<PlutusDataByteString>(actual).Value.ToArray());
                break;
            default:
                Assert.Fail($"Unexpected case type {expected.GetType().Name}");
                break;
        }
    }
}
