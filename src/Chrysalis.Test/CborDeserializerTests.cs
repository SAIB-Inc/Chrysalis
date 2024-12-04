using System.Text;
using Chrysalis.Converters;
using Chrysalis.Types;
using Chrysalis.Types.Custom;
using Xunit;

namespace Chrysalis.Test;

public class CborDeserializerTests
{
    // union test
    public static IEnumerable<object[]> ConstrData =>
    [
        ["d87a80", new CborTrue()],
        ["d87980", new CborFalse()],
    ];

    [Theory]
    [MemberData(nameof(ConstrData))]
    public void DeserializeConstrTest(string hex, ICbor expectedValue)
    {
        byte[] data = Convert.FromHexString(hex);
        ICbor result = CborSerializer.Deserialize<CardanoBool>(data);
        Console.WriteLine($"Expected: {expectedValue}");
        Console.WriteLine($"Actual: {result}");
        Assert.StrictEqual(result, expectedValue);
    }
}
