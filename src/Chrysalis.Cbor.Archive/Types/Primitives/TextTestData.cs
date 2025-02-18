using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Test.Types.Primitives;

public record InheritedText(string Value) : CborText(Value);
public record NestedInheritedText(string Value) : InheritedText(Value);

public class TextTestData
{
    private const string _testTag = "CborText";

    public static IEnumerable<TestData> TestData
    {
        get
        {
            yield return new("Empty", "60", new CborText(""));
            yield return new("Inherited Empty", "60", new InheritedText(""));
            yield return new("Nested Inherited Empty", "60", new NestedInheritedText(""));

            yield return new("Single Char", "6161", new CborText("a"));
            yield return new("Inherited Single Char", "6161", new InheritedText("a"));
            yield return new("Nested Inherited Single Char", "6161", new NestedInheritedText("a"));

            yield return new("Short ASCII", "6568656C6C6F", new CborText("hello"));
            yield return new("Inherited Short ASCII", "6568656C6C6F", new InheritedText("hello"));
            yield return new("Nested Inherited Short ASCII", "6568656C6C6F", new NestedInheritedText("hello"));
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