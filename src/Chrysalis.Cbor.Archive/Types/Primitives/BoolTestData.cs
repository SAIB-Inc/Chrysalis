namespace Chrysalis.Test.Types.Primitives;

public record InheritedBool(bool Value) : CborBool(Value);
public record NestedInheritedBool(bool Value) : InheritedBool(Value);

public class BoolTestData
{
    public readonly static string _testTag = "CborBool";

    public static IEnumerable<TestData> TestData
    {
        get
        {
            yield return new("Basic False", "f4", new CborBool(false));
            yield return new("Basic True", "f5", new CborBool(true));
            yield return new("Inherited False", "f4", new InheritedBool(false));
            yield return new("Inherited True", "f5", new InheritedBool(true));
            yield return new("Nested Inherited False", "f4", new NestedInheritedBool(false));
            yield return new("Nested Inherited True", "f5", new NestedInheritedBool(true));
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