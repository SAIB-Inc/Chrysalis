using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed class FlatDecoderTests
{
    [Theory]
    [InlineData("0101004bd601", true, true)]         // 7 5 0
    [InlineData("0101004bded0800001", true, true)]   // 7 7 6 0 0
    [InlineData("0101004bf201", true, true)]         // 7 12 0
    [InlineData("0101004e81", true, true)]           // 13
    [InlineData("0101004981", true, false)]          // 3
    [InlineData("0101004ac001", false, false)]       // 5 0
    [InlineData("0101004b42000001", false, false)]   // 6 0 0
    [InlineData("0101004bda10000001", false, false)] // 7 6 0 0
    [InlineData("0101004bdeb081", false, false)]     // 7 7 5 0 0
    [InlineData("010100484001", false, false)]       // 0 0
    [InlineData("0101004e4001", false, false)]       // 12 0
    [InlineData("0101004f01", false, false)]         // 14
    [InlineData("0101004bd5", false, false)]         // 7 5
    public void ConstantTypesFollowTheUpstreamGrammar(string hex, bool valid, bool roundTrips)
    {
        byte[] bytes = Convert.FromHexString(hex);
        if (!valid)
        {
            _ = Assert.Throws<InvalidOperationException>(() => FlatDecoder.DecodeProgram(bytes));
            return;
        }
        Program<DeBruijn> program = FlatDecoder.DecodeProgram(bytes);
        if (roundTrips)
        {
            Assert.Equal(bytes, FlatEncoder.EncodeProgram(program));
        }
    }

    [Fact]
    public void RawDecoderReadsWord64WithoutLimits()
    {
        NodeBudgetTests.NodeData fixture = NodeBudgetTests.Fixture;
        byte[] Failure(string id) => fixture.Failures.Single(row => row.Script.Id == id).Script.Flat;
        byte[] Accepted(string id) => fixture.Scripts.Single(script => script.Id == id).Flat;

        _ = Assert.Throws<InvalidOperationException>(() => FlatDecoder.DecodeProgram(Failure("F-var-11groups-V3")));
        _ = Assert.Throws<InvalidOperationException>(() => FlatDecoder.DecodeProgram(Failure("F-var-10th-group-2-V3")));
        _ = Assert.Throws<InvalidOperationException>(() => FlatDecoder.DecodeProgram(Failure("F-constr-tag-11groups-V3")));

        CaseTerm<DeBruijn> varCase = Assert.IsType<CaseTerm<DeBruijn>>(
            Assert.IsType<LambdaTerm<DeBruijn>>(FlatDecoder.DecodeProgram(Accepted("F-var-2^64-1-V3")).Term).Body);
        Assert.Equal(int.MaxValue, Assert.IsType<VarTerm<DeBruijn>>(varCase.Branches[1]).Name.Index);

        CaseTerm<DeBruijn> constrCase = Assert.IsType<CaseTerm<DeBruijn>>(
            Assert.IsType<LambdaTerm<DeBruijn>>(FlatDecoder.DecodeProgram(Accepted("F-constr-tag-2^64-1-untaken-V3")).Term).Body);
        Assert.Equal(ulong.MaxValue, Assert.IsType<ConstrTerm<DeBruijn>>(constrCase.Branches[1]).Tag);

        Assert.NotNull(FlatDecoder.DecodeProgram(Failure("P-C2-type-33-tags")));
        Assert.NotNull(FlatDecoder.DecodeProgram(Failure("P-C4-constr-1025-fields")));
    }

    // Plutus's Flat form for value constants: the suite's own bytes, read and written back unchanged.
    [Fact]
    public void ValueConstantsRoundTripThroughFlat()
    {
        int inputs = 0;
        int results = 0;
        foreach (object row in ConformanceTests.DiscoverTests())
        {
            object?[] cells = row is ITheoryDataRow data ? data.GetData() : (object?[])row;
            if (cells is not [string testCase, "flat"])
            {
                continue;
            }
            string flat = ConformanceTests.Input(testCase, "flat");
            string twin = ConformanceTests.Input(testCase, "uplc");
            foreach ((string path, string text) in new[] { (flat, twin), (flat + ".expected", twin + ".expected") })
            {
                if (!File.Exists(text) || !HoldsValue(text))
                {
                    continue;
                }
                byte[] bytes = File.ReadAllBytes(path);
                Assert.True(FlatEncoder.EncodeProgram(FlatDecoder.DecodeProgram(bytes)).AsSpan().SequenceEqual(bytes), path);
                if (path == flat)
                {
                    inputs++;
                }
                else
                {
                    results++;
                }
            }
        }
        Assert.Equal((51, 30), (inputs, results));
    }

    private static bool HoldsValue(string path) =>
        string.Join(' ', File.ReadAllText(path).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Contains("(con value", StringComparison.Ordinal);

    // Value.hs's buildValueWith, and G1.hs:94, G2.hs:80 and Pairing.hs:62 (encode = error).
    public static TheoryData<Constant, string> RefusedConstants() => new()
    {
        { new ValueConstant(new LedgerValue([Currency(0xbb), Currency(0xaa)])), "Flat: cannot encode value constant: currency symbols not strictly ascending" },
        { new ValueConstant(new LedgerValue([Currency(0xaa, 0)])), "Flat: cannot encode value constant: zero quantity" },
        { new Bls12381G1Constant(new byte[48]), "Flat: cannot encode constant Bls12381G1Constant" },
        { new Bls12381G2Constant(new byte[96]), "Flat: cannot encode constant Bls12381G2Constant" },
        { new Bls12381MlResultConstant(new byte[1]), "Flat: cannot encode constant Bls12381MlResultConstant" }
    };

    private static CurrencyEntry Currency(byte key, int quantity = 1) => new(new byte[] { key }, [new TokenEntry(new byte[1], quantity)]);

    [Theory]
    [MemberData(nameof(RefusedConstants))]
    public void FlatEncoderRefusesWhatTheNodeRefuses(Constant constant, string message)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => FlatEncoder.EncodeProgram(new Program<DeBruijn>(new(1, 1, 0), new ConstTerm<DeBruijn>(constant))));
        Assert.StartsWith(message, error.Message, StringComparison.Ordinal);
    }

    private static string Repeat(string hex, int count) => string.Concat(Enumerable.Repeat(hex, count));

    // Each row is (program 1.0.0 (con ...)) and ends with the program's filler 01; the canonical bytes, or null where plutus refuses it.
    public static TheoryData<string, string?> FlatConstantRows() => new()
    {
        { "010000488101000100" + "1e" + Repeat("00", 30) + "0001", "0100004881" + "20" + Repeat("00", 32) + "0001" },
        { "01000048a0" + "20" + Repeat("00", 32) + "0001", null },
        { "0100004901" + "01c3" + "01a9" + "1e" + Repeat("61", 30) + "0001", "0100004901" + "20" + "c3a9" + Repeat("61", 30) + "0001" },
        { "0100004901" + "01c3" + "0128" + "1e" + Repeat("61", 30) + "0001", null },
        { "010000490102c3280001", null },
        { "0100004c81" + "30" + "c0" + Repeat("00", 47) + "0001", null },
        { "0100004d01" + "60" + "c0" + Repeat("00", 95) + "0001", null },
        { "0100004d8101000001", null },
        { "0100004bd721", "0100004bd721" },
        { "0100004bd741", "0100004bd741" },
        { "0100004bd761", "0100004bd761" }
    };

    [Theory]
    [MemberData(nameof(FlatConstantRows))]
    public void FlatConstantsAreReadAsTheNodeReadsThem(string hex, string? canonical)
    {
        byte[] bytes = Convert.FromHexString(hex);
        if (canonical is null)
        {
            _ = Assert.Throws<InvalidOperationException>(() => FlatDecoder.DecodeProgram(bytes));
            return;
        }
        Assert.Equal(canonical, Convert.ToHexString(FlatEncoder.EncodeProgram(FlatDecoder.DecodeProgram(bytes))), ignoreCase: true);
    }
}
