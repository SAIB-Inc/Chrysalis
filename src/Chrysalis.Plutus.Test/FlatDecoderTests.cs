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
    [InlineData("0101004981", true, false)]          // 3
    [InlineData("0101004ac001", false, false)]       // 5 0
    [InlineData("0101004b42000001", false, false)]   // 6 0 0
    [InlineData("0101004bda10000001", false, false)] // 7 6 0 0
    [InlineData("0101004bdeb081", false, false)]     // 7 7 5 0 0
    [InlineData("010100484001", false, false)]       // 0 0
    [InlineData("0101004e4001", false, false)]       // 12 0
    [InlineData("0101004e81", false, false)]         // 13
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
}
