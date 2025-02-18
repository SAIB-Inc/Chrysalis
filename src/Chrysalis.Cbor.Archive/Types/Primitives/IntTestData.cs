using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Test.Types.Primitives;

public record InheritedInt(int Value) : CborInt(Value);
public record NestedInheritedInt(int Value) : InheritedInt(Value);

public class IntTestData
{
    public readonly static string _testTag = "CborInt";

    public static IEnumerable<TestData> TestData
    {
        get
        {
            yield return new("Basic Zero", "00", new CborInt(0));
            yield return new("Basic One", "01", new CborInt(1));
            yield return new("Basic Negative One", "20", new CborInt(-1));
            yield return new("Basic Eight", "08", new CborInt(8));
            yield return new("Basic Ten", "0a", new CborInt(10));
            yield return new("Basic Negative Ten", "29", new CborInt(-10));
            yield return new("Basic 23", "17", new CborInt(23));

            // Inherited
            yield return new("Inherited Zero", "00", new InheritedInt(0));
            yield return new("Inherited One", "01", new InheritedInt(1));
            yield return new("Inherited Negative One", "20", new InheritedInt(-1));
            yield return new("Inherited Eight", "08", new InheritedInt(8));
            yield return new("Inherited Ten", "0a", new InheritedInt(10));
            yield return new("Inherited Negative Ten", "29", new InheritedInt(-10));
            yield return new("Inherited 23", "17", new InheritedInt(23));

            // Nested Inherited
            yield return new("Nested Inherited Zero", "00", new NestedInheritedInt(0));
            yield return new("Nested Inherited One", "01", new NestedInheritedInt(1));
            yield return new("Nested Inherited Negative One", "20", new NestedInheritedInt(-1));
            yield return new("Nested Inherited Eight", "08", new NestedInheritedInt(8));
            yield return new("Nested Inherited Ten", "0a", new NestedInheritedInt(10));
            yield return new("Nested Inherited Negative Ten", "29", new NestedInheritedInt(-10));
            yield return new("Nested Inherited 23", "17", new NestedInheritedInt(23));
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
