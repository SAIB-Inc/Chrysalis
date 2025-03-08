using VerifyXunit;
using Xunit;

namespace Chrysalis.Test;

public class CborGeneratorTest
{
    [Fact]
    public Task GeneratesEnumExtensionsCorrectly()
    {
        // The source code to test
        var source = @"
using NetEscapades.EnumGenerators;

[EnumExtensions]
public enum Colour
{
    Red = 0,
    Blue = 1,
}";

        // Pass the source code to our helper and snapshot test the output
        return TestHelper.Verify(source);
    }
}