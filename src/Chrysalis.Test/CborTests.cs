using System.Reflection;
using System.Text.Json;
using Chrysalis.Cardano.Crashr.Types.Datums;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Types;
using Chrysalis.Test.Types;
using Chrysalis.Test.Types.Cardano.Crashr;
using Chrysalis.Test.Types.Primitives;
using Xunit;

namespace Chrysalis.Test;

public class CborTests
{
    [Theory]
    [MemberData(nameof(BoolTestData.GetTestData), MemberType = typeof(BoolTestData))]
    [MemberData(nameof(BytesTestData.GetTestData), MemberType = typeof(BytesTestData))]
    [MemberData(nameof(IntTestData.GetTestData), MemberType = typeof(IntTestData))]
    [MemberData(nameof(LongTestData.GetTestData), MemberType = typeof(LongTestData))]
    [MemberData(nameof(UlongTestData.GetTestData), MemberType = typeof(UlongTestData))]
    [MemberData(nameof(TextTestData.GetTestData), MemberType = typeof(TextTestData))]
    public void Deserialize(TestData testData)
    {
        Assert.NotNull(testData);
        Assert.NotNull(testData.Serialized);
        Assert.NotNull(testData.Deserialized);

        byte[] data = Convert.FromHexString(testData.Serialized);
        Type actualType = testData.Deserialized.GetType();

        // Act
        MethodInfo deserializeMethod = typeof(CborSerializer)
            .GetMethod(nameof(CborSerializer.Deserialize))!
            .MakeGenericMethod(actualType);

        object? actual = deserializeMethod.Invoke(null, [data]);

        // Assert
        Assert.NotNull(actual);
        Assert.IsType(actualType, actual);
        Assert.Equivalent(testData.Deserialized, actual);
    }

    [Theory]
    [MemberData(nameof(CrashrTestData.GetTestData), MemberType = typeof(CrashrTestData))]
    public void DeserializeCrashr(string testName, string serialized, CborBase deserialized)
    {
        Assert.NotNull(serialized);
        Assert.NotNull(deserialized);

        byte[] data = Convert.FromHexString(serialized);
        Type actualType = deserialized.GetType();

        // Act
        MethodInfo deserializeMethod = typeof(CborSerializer)
            .GetMethod(nameof(CborSerializer.Deserialize))!
            .MakeGenericMethod(actualType);

        object? actual = deserializeMethod.Invoke(null, [data]);

        // Assert
        Assert.NotNull(actual);
        Assert.IsType(actualType, actual);
        CrashrTestData.AssertListingDatumsEqual((ListingDatum)deserialized, (ListingDatum)actual);
    }
}
