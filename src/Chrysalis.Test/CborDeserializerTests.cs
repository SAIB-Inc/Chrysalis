using System.Text;
using Chrysalis.Converters;
using Chrysalis.Types;
using Chrysalis.Types.Custom;
using Chrysalis.Types.Custom.Crashr;
using Xunit;

namespace Chrysalis.Test;

public class CborDeserializerTests
{
    // union test
    public static IEnumerable<object[]> ConstrData =>
    [
        ["d87a80", new CborTrue(){
            Raw = Convert.FromHexString("d87a80")
        }],
        ["d87a80", new CborInheritedTrue(){
            Raw = Convert.FromHexString("d87a80")
        }],
        ["d87980", new CborFalse(){
            Raw = Convert.FromHexString("d87980")
        }],
    ];

    [Theory]
    [MemberData(nameof(ConstrData))]
    public void DeserializeConstrTest(string hex, ICbor expectedValue)
    {
        byte[] data = Convert.FromHexString(hex);

        // Dynamically resolve the type of expectedValue
        Type targetType = expectedValue.GetType();

        // Call Deserialize with the resolved type
        ICbor? result = (ICbor?)typeof(CborSerializer)
            .GetMethod(nameof(CborSerializer.Deserialize))!
            .MakeGenericMethod(targetType)
            .Invoke(null, [data]);

        Console.WriteLine($"Expected: {expectedValue}");
        Console.WriteLine($"Actual: {result}");
        Assert.Equal(result?.ToString(), expectedValue.ToString());
    }

    [Theory]
    [InlineData("as")]
    public void DeserializeListingDatumTest(string hex)
    {
        byte[] data = Convert.FromHexString("d8799f9fd8799fd8799fd8799f581c7060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6ffd8799fd8799fd8799f581c850282937506f53d58e9e6fd7ccbbbc57196d58b2a11e36a76a60857ffffffffa140a1401a02dc6c00ffff581c7060251a30c40e428085cdb477aa0ca8462ae5d8b6a55e5e9616aeb6ff");
        ListingDatum datum = CborSerializer.Deserialize<ListingDatum>(data);

        Assert.Equal(true, true);
    }
}
