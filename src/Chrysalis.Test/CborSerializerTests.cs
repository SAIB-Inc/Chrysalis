
using System.Text;
using Chrysalis.Converters;
using Chrysalis.Types;
using Chrysalis.Types.Custom;
using Xunit;

namespace Chrysalis.Test;

public class CborSerializerTests
{
    [Theory]
    [InlineData(true, "f5")]
    [InlineData(false, "f4")]
    public void SerializeBoolTest(bool value, string expectedValue)
    {
        CborBool result = new(value);
        string hex = Convert.ToHexString(CborSerializer.Serialize(result)).ToLowerInvariant();
        Assert.Equal(hex, expectedValue.ToLowerInvariant());
    }

    [Theory]
    [InlineData("hello", "4568656c6c6f")]
    public void SerializeBytesTest(string stringValue, string expectedValue)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(stringValue);
        CborBytes result = new(inputBytes);
        string hex = Convert.ToHexString(CborSerializer.Serialize(result)).ToLowerInvariant();
        Assert.Equal(hex, expectedValue.ToLowerInvariant());
    }

    [Theory]
    [InlineData(52, "1834")]
    public void SerializeIntTest(int value, string expectedValue)
    {
        CborInt result = new(value);
        string hex = Convert.ToHexString(CborSerializer.Serialize(result)).ToLowerInvariant();
        Assert.Equal(hex, expectedValue.ToLowerInvariant());
    }

    [Theory]
    [InlineData(1_000_000UL, "1a000f4240")]
    public void SerializeUlongTest(ulong value, string expectedValue)
    {
        CborUlong result = new(value);
        string hex = Convert.ToHexString(CborSerializer.Serialize(result)).ToLowerInvariant();
        Assert.Equal(hex, expectedValue.ToLowerInvariant());
    }

    public static IEnumerable<object[]> MapData =>
    [
        [new CborMap<CborBytes, CborBytes>(new Dictionary<CborBytes, CborBytes> { { new(Convert.FromHexString("61")), new(Convert.FromHexString("41696b656e")) } }), "a141614541696b656e"]
    ];

    [Theory]
    [MemberData(nameof(MapData))]
    public void SerializeMapTest(CborMap<CborBytes, CborBytes> value, string expectedValue)
    {
        string hex = Convert.ToHexString(CborSerializer.Serialize(value)).ToLowerInvariant();
        Assert.Equal(hex, expectedValue.ToLowerInvariant());
    }

    public static IEnumerable<object[]> DefArrayData =>
    [
        [new CborDefList<CborInt>([new(1), new(2), new(3), new(4), new(5)]), "850102030405"]
    ];

    [Theory]
    [MemberData(nameof(DefArrayData))]
    public void SerializeArrayTest(CborDefList<CborInt> value, string expectedValue)
    {
        string hex = Convert.ToHexString(CborSerializer.Serialize(value)).ToLowerInvariant();
        Assert.Equal(hex, expectedValue.ToLowerInvariant());
    }

    public static IEnumerable<object[]> ConstrData =>
    [
        [new CborTrue(), "d87a80"],
        [new CborFalse(), "d87980"],
    ];

    // test constr
    [Theory]
    [MemberData(nameof(ConstrData))]
    public void SerializeConstrTest(CborConstr value, string expectedValue)
    {
        string hex = Convert.ToHexString(CborSerializer.Serialize(value)).ToLowerInvariant();
        Assert.Equal(hex, expectedValue.ToLowerInvariant());
    }
}