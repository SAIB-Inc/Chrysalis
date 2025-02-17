using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Test.Types.Primitives;

public record InheritedUlong(ulong Value) : CborUlong(Value);
public record NestedInheritedUlong(ulong Value) : CborUlong(Value);

public class UlongTestData
{
    private const string _testTag = "CborUlong";

    public static IEnumerable<TestData> TestData
    {
        get
        {
            // Zero
            yield return new("Zero", "00", new CborUlong(0));
            yield return new("Inherited Zero", "00", new InheritedUlong(0));
            yield return new("Nested Inherited Zero", "00", new NestedInheritedUlong(0));

            // Small positive (0-23) 
            yield return new("Small 10", "0a", new CborUlong(10));
            yield return new("Inherited Small 10", "0a", new InheritedUlong(10));
            yield return new("Nested Inherited Small 10", "0a", new NestedInheritedUlong(10));

            yield return new("Small 23", "17", new CborUlong(23));
            yield return new("Inherited Small 23", "17", new InheritedUlong(23));
            yield return new("Nested Inherited Small 23", "17", new NestedInheritedUlong(23));

            // One byte (24-255)
            yield return new("One Byte 24", "1818", new CborUlong(24));
            yield return new("Inherited One Byte 24", "1818", new InheritedUlong(24));
            yield return new("Nested Inherited One Byte 24", "1818", new NestedInheritedUlong(24));

            yield return new("One Byte 255", "18ff", new CborUlong(255));
            yield return new("Inherited One Byte 255", "18ff", new InheritedUlong(255));
            yield return new("Nested Inherited One Byte 255", "18ff", new NestedInheritedUlong(255));

            // Two bytes (256-65535)
            yield return new("Two Bytes 256", "190100", new CborUlong(256));
            yield return new("Inherited Two Bytes 256", "190100", new InheritedUlong(256));
            yield return new("Nested Inherited Two Bytes 256", "190100", new NestedInheritedUlong(256));

            yield return new("Two Bytes 65535", "19ffff", new CborUlong(65535));
            yield return new("Inherited Two Bytes 65535", "19ffff", new InheritedUlong(65535));
            yield return new("Nested Inherited Two Bytes 65535", "19ffff", new NestedInheritedUlong(65535));

            // Four bytes (65536-4294967295)
            yield return new("Four Bytes 65536", "1a00010000", new CborUlong(65536));
            yield return new("Inherited Four Bytes 65536", "1a00010000", new InheritedUlong(65536));
            yield return new("Nested Inherited Four Bytes 65536", "1a00010000", new NestedInheritedUlong(65536));

            yield return new("Four Bytes Max", "1affffffff", new CborUlong(4294967295));
            yield return new("Inherited Four Bytes Max", "1affffffff", new InheritedUlong(4294967295));
            yield return new("Nested Inherited Four Bytes Max", "1affffffff", new NestedInheritedUlong(4294967295));

            // Eight bytes (4294967296-18446744073709551615)
            yield return new("Eight Bytes Min", "1b0000000100000000", new CborUlong(4294967296));
            yield return new("Inherited Eight Bytes Min", "1b0000000100000000", new InheritedUlong(4294967296));
            yield return new("Nested Inherited Eight Bytes Min", "1b0000000100000000", new NestedInheritedUlong(4294967296));

            // Large number
            yield return new("Eight Bytes Large", "1b002386f26fc10000", new CborUlong(10000000000000000));
            yield return new("Inherited Eight Bytes Large", "1b002386f26fc10000", new InheritedUlong(10000000000000000));
            yield return new("Nested Inherited Eight Bytes Large", "1b002386f26fc10000", new NestedInheritedUlong(10000000000000000));

            // Edge case - max ulong
            yield return new("Max ULong", "1bffffffffffffffff", new CborUlong(ulong.MaxValue));
            yield return new("Inherited Max ULong", "1bffffffffffffffff", new InheritedUlong(ulong.MaxValue));
            yield return new("Nested Inherited Max ULong", "1bffffffffffffffff", new NestedInheritedUlong(ulong.MaxValue));
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