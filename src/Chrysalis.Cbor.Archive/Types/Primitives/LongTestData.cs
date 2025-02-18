using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Test.Types.Primitives;

public record InheritedLong(long Value) : CborLong(Value);
public record NestedInheritedLong(long Value) : InheritedLong(Value);

public class LongTestData
{
    public readonly static string _testTag = "CborLong";

    public static IEnumerable<TestData> TestData
    {
        get
        {
            // Zero
            yield return new("Zero", "00", new CborLong(0));
            yield return new("Inherited Zero", "00", new InheritedLong(0));
            yield return new("Nested Inherited Zero", "00", new NestedInheritedLong(0));

            // Small positive integers
            yield return new("Small Positive 10", "0a", new CborLong(10));
            yield return new("Inherited Small Positive 10", "0a", new InheritedLong(10));
            yield return new("Nested Inherited Small Positive 10", "0a", new NestedInheritedLong(10));

            // One byte positive
            yield return new("One Byte 100", "1864", new CborLong(100));
            yield return new("Inherited One Byte 100", "1864", new InheritedLong(100));
            yield return new("Nested Inherited One Byte 100", "1864", new NestedInheritedLong(100));

            // Two bytes positive
            yield return new("Two Bytes 1000", "1903e8", new CborLong(1000));
            yield return new("Inherited Two Bytes 1000", "1903e8", new InheritedLong(1000));
            yield return new("Nested Inherited Two Bytes 1000", "1903e8", new NestedInheritedLong(1000));

            // Four bytes positive
            yield return new("Four Bytes 1000000", "1a000f4240", new CborLong(1000000));
            yield return new("Inherited Four Bytes 1000000", "1a000f4240", new InheritedLong(1000000));
            yield return new("Nested Inherited Four Bytes 1000000", "1a000f4240", new NestedInheritedLong(1000000));

            // Eight bytes positive
            yield return new("Eight Bytes Large", "1b002386f26fc10000", new CborLong(10000000000000000));
            yield return new("Inherited Eight Bytes Large", "1b002386f26fc10000", new InheritedLong(10000000000000000));
            yield return new("Nested Inherited Eight Bytes Large", "1b002386f26fc10000", new NestedInheritedLong(10000000000000000));

            // Small negative
            yield return new("Negative Ten", "29", new CborLong(-10));
            yield return new("Inherited Negative Ten", "29", new InheritedLong(-10));
            yield return new("Nested Inherited Negative Ten", "29", new NestedInheritedLong(-10));

            // One byte negative
            yield return new("One Byte -100", "3863", new CborLong(-100));
            yield return new("Inherited One Byte -100", "3863", new InheritedLong(-100));
            yield return new("Nested Inherited One Byte -100", "3863", new NestedInheritedLong(-100));

            // Two bytes negative
            yield return new("Two Bytes -1000", "3903e7", new CborLong(-1000));
            yield return new("Inherited Two Bytes -1000", "3903e7", new InheritedLong(-1000));
            yield return new("Nested Inherited Two Bytes -1000", "3903e7", new NestedInheritedLong(-1000));

            // Four bytes negative
            yield return new("Four Bytes -1000000", "3a000f423f", new CborLong(-1000000));
            yield return new("Inherited Four Bytes -1000000", "3a000f423f", new InheritedLong(-1000000));
            yield return new("Nested Inherited Four Bytes -1000000", "3a000f423f", new NestedInheritedLong(-1000000));

            // Edge cases
            yield return new("Max Long", "1b7fffffffffffffff", new CborLong(long.MaxValue));
            yield return new("Inherited Max Long", "1b7fffffffffffffff", new InheritedLong(long.MaxValue));
            yield return new("Nested Inherited Max Long", "1b7fffffffffffffff", new NestedInheritedLong(long.MaxValue));

            yield return new("Min Long", "3b7fffffffffffffff", new CborLong(long.MinValue));
            yield return new("Inherited Min Long", "3b7fffffffffffffff", new InheritedLong(long.MinValue));
            yield return new("Nested Inherited Min Long", "3b7fffffffffffffff", new NestedInheritedLong(long.MinValue));
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