using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Test.Types.Primitives;

public record InheritedBytes(byte[] Value) : CborBytes(Value);
public record NestedInheritedBytes(byte[] Value) : InheritedBytes(Value);

public class BytesTestData
{
    public readonly static string _testTag = "CborBytes";

    public static IEnumerable<TestData> TestData
    {
        get
        {
            // Empty bytes
            yield return new("Empty bytes", "40", new CborBytes(Convert.FromHexString("")));

            // Single byte
            yield return new("Single byte", "41FF", new CborBytes(Convert.FromHexString("FF")));

            // Multiple bytes with different patterns
            yield return new("Three bytes", "43AABBCC", new CborBytes(Convert.FromHexString("AABBCC")));
            yield return new("Alternating pattern", "43FF00FF", new CborBytes(Convert.FromHexString("FF00FF")));

            // Testing different length encodings
            yield return new("23 bytes (max single byte length)",
                "57" + "FF".PadRight(46, '0'),
                new CborBytes(Convert.FromHexString("FF".PadRight(46, '0'))));

            yield return new("24 bytes (needs two bytes for length)",
                "5818" + "FF".PadRight(48, '0'),
                new CborBytes(Convert.FromHexString("FF".PadRight(48, '0'))));

            // Test inheritance
            yield return new("Inherited type", "43AABBCC", new InheritedBytes(Convert.FromHexString("AABBCC")));
            yield return new("Nested inherited type", "43AABBCC", new NestedInheritedBytes(Convert.FromHexString("AABBCC")));
        }
    }

    public static IEnumerable<object[]> GetTestData()
    {
        return TestData.Select(testData =>
        {
            testData.Description = $"[{_testTag}] {testData.Description}";
            return new object[] { testData };
        });
    }
}