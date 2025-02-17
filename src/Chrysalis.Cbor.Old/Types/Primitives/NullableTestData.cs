using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Test.Types.Primitives;

public record InheritedNullable<T>(T? Value) : CborNullable<T>(Value) where T : CborBase;
public record NestedInheritedNullable<T>(T? Value) : InheritedNullable<T>(Value) where T : CborBase;

public class NullableTestData
{
    public readonly static string _testTag = "CborNullable";

    public static IEnumerable<TestData> TestData
    {
        get
        {
            // Test with int
            yield return new("Basic Null Int", "f6", new CborNullable<CborInt>(null));
            yield return new("Basic Value Int", "01", new CborNullable<CborInt>(new CborInt(1)));
            yield return new("Inherited Null Int", "f6", new InheritedNullable<CborInt>(null));
            yield return new("Inherited Value Int", "01", new InheritedNullable<CborInt>(new CborInt(1)));
            yield return new("Nested Inherited Null Int", "f6", new NestedInheritedNullable<CborInt>(null));
            yield return new("Nested Inherited Value Int", "01", new NestedInheritedNullable<CborInt>(new CborInt(1)));
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