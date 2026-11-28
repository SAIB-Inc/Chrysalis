using Chrysalis.Plutus.Text;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed class ParserTests
{
    // Plutus reads every bracketed literal as inBrackets (p `sepBy` symbol ","), which reads an item after each comma (Parser/Builtin.hs:102-109,119-127,181-183).
    [Theory]
    [InlineData("(con (list integer) [1,])")]
    [InlineData("(con (array integer) [1,])")]
    [InlineData("(con value [(#aa, [(#01, 1)]),])")]
    [InlineData("(con value [(#aa, [(#01, 1),])])")]
    [InlineData("(con data (List [I 1,]))")]
    [InlineData("(con data (Map [(I 1, I 2),]))")]
    [InlineData("(con data (Constr 0 [I 1,]))")]
    public void ListLiteralsRefuseATrailingComma(string constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        _ = UplcParser.Parse($"(program 1.1.0 {constant.Replace(",]", "]", StringComparison.Ordinal)})");
        _ = Assert.Throws<ParseException>(() => UplcParser.Parse($"(program 1.1.0 {constant})"));
    }
}
