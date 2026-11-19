using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Text;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Plutus.VM.Models;
using Xunit;

namespace Chrysalis.Plutus.Test;

public class EvaluatorTests
{
    private static readonly Term<DeBruijn> IntegerOne = new ConstTerm<DeBruijn>(new IntegerConstant(1));

    [Fact]
    public void EvaluateScript_RejectsNonUnitResults_OutsidePlutusV1AndV2()
    {
        byte[] integerScript = Encode(IntegerOne);
        byte[] delayScript = Encode(new DelayTerm<DeBruijn>(IntegerOne));
        byte[] dataScript = Encode(new ConstTerm<DeBruijn>(new DataConstant(new PlutusDataConstr(0, []))));
        byte[][] cborArguments = [];
        PlutusData[] vmArguments = [];

        Assert.All(new (string Case, Exception? Error)[]
        {
            ("V3 integer, CBOR", Record.Exception(() => Evaluator.EvaluateScript(integerScript, cborArguments, 3, 11))),
            ("V3 integer, VM", Record.Exception(() => Evaluator.EvaluateScript(integerScript, vmArguments, 3, 11))),
            ("V3 non-constant, CBOR", Record.Exception(() => Evaluator.EvaluateScript(delayScript, cborArguments, 3, 11))),
            ("V3 non-constant, VM", Record.Exception(() => Evaluator.EvaluateScript(delayScript, vmArguments, 3, 11))),
            ("V3 data constructor, CBOR", Record.Exception(() => Evaluator.EvaluateScript(dataScript, cborArguments, 3, 11))),
            ("V3 data constructor, VM", Record.Exception(() => Evaluator.EvaluateScript(dataScript, vmArguments, 3, 11))),
            ("Later language, CBOR", Record.Exception(() => Evaluator.EvaluateScript(integerScript, cborArguments, plutusVersion: 4, protocolVersion: 11))),
            ("Later language, VM", Record.Exception(() => Evaluator.EvaluateScript(integerScript, vmArguments, plutusVersion: 4, protocolVersion: 11)))
        }, result => Assert.IsType(result.Case.StartsWith("Later language", StringComparison.Ordinal)
            ? typeof(ArgumentOutOfRangeException) : typeof(InvalidReturnValueException), result.Error));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EvaluateScript_RunsPlutusV1AndV2WithTheirArray(int version)
    {
        byte[] script = Encode(IntegerOne);
        byte[][] cbor = [];
        PlutusData[] vm = [];
        long[] costs = NodeBudgetTests.Fixture.Models[version - 1];
        ExUnitsResult expected = Evaluator.EvaluateScript(script, cbor, version, 11, costs).ExUnits;
        Assert.Equal(expected, Evaluator.EvaluateScript(script, vm, version, 11, costs).ExUnits);
        Assert.True(expected.Steps > 0);
    }

    [Theory]
    [InlineData(1, 11)]
    [InlineData(2, 11)]
    [InlineData(3, 9)]
    [InlineData(3, 10)]
    public void EvaluateScript_RequiresAnArrayExceptForLatestV3(int language, int pv)
    {
        byte[] script = Encode(new ConstTerm<DeBruijn>(UnitConstant.Instance));
        _ = Assert.Throws<ArgumentNullException>(() => Evaluator.EvaluateScript(script, Array.Empty<byte[]>(), language, pv));
        _ = Assert.Throws<ArgumentNullException>(() => Evaluator.EvaluateScript(script, Array.Empty<PlutusData>(), language, pv));
    }

    [Theory]
    [InlineData(0, 11, typeof(ArgumentOutOfRangeException))]
    [InlineData(-1, 11, typeof(ArgumentOutOfRangeException))]
    [InlineData(4, 11, typeof(ArgumentOutOfRangeException))]
    [InlineData(int.MaxValue, 11, typeof(ArgumentOutOfRangeException))]
    [InlineData(3, -1, typeof(ArgumentOutOfRangeException))]
    [InlineData(3, 12, typeof(NotSupportedException))]
    [InlineData(3, int.MaxValue, typeof(NotSupportedException))]
    public void EvaluateScript_ValidatesLanguageAndProtocolVersion(int language, int pv, Type error)
    {
        byte[] script = Encode(new ConstTerm<DeBruijn>(UnitConstant.Instance));
        _ = Assert.Throws(error, () => Evaluator.EvaluateScript(script, Array.Empty<byte[]>(), language, pv));
        _ = Assert.Throws(error, () => Evaluator.EvaluateScript(script, Array.Empty<PlutusData>(), language, pv));
    }

    [Fact]
    public void EvaluateScript_RejectsFormerDefaultCostModelCall() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Evaluator.EvaluateScript(Encode(IntegerOne), Array.Empty<PlutusData>(), default, 3));

    [Fact]
    public void EvaluateScript_UsesTheSuppliedArray()
    {
        byte[] script = NodeBudgetTests.Fixture.Scripts.Single(s => s.Id == "P-B01-i2b-size-8192").Flat;
        byte[][] cborArguments = [[0xD8, 0x79, 0x80]]; // Constr 0 []
        PlutusData[] vmArguments = [new PlutusDataConstr(0, [])];
        long[] costs = (long[])NodeBudgetTests.Fixture.Models[2].Clone();
        string[] names = CostModelTests.DeployedNames(3);
        int cpu = Array.IndexOf(names, "cekStartupCost-exBudgetCPU");
        int mem = Array.IndexOf(names, "cekStartupCost-exBudgetMemory");

        Check(new(2424, 1530707));
        costs[cpu]++;
        costs[mem]++;
        Check(new(2425, 1530708));
        costs[cpu] = long.MaxValue;
        Check(new(2425, long.MaxValue));

        void Check(ExUnitsResult expected)
        {
            Assert.Equal(expected, Evaluator.EvaluateScript(script, cborArguments, 3, 11, costs).ExUnits);
            Assert.Equal(expected, Evaluator.EvaluateScript(script, vmArguments, 3, 11, costs).ExUnits);
        }
    }

    [Fact]
    public void EvaluateScript_UsesTheV3ChainModelWhenTheArrayIsNull()
    {
        const string source = "(program 1.0.0 [(force (builtin ifThenElse)) [(builtin equalsByteString) (con bytestring #00) (con bytestring #00)] (con unit ()) (con unit ())])";
        Term<DeBruijn> term = UplcParser.NameToDeBruijn(UplcParser.Parse(source)).Term;
        byte[] script = Encode(term);
        byte[][] cbor = [];
        PlutusData[] vm = [];
        ExUnitsResult expected = Evaluator.EvaluateScript(script, vm, 3, 11, DefaultCosts.LatestV3).ExUnits;
        Assert.NotEqual(expected, Evaluator.Run(term, CostModel.Default, 3, null).ExUnits);
        Assert.Equal(expected, Evaluator.EvaluateScript(script, cbor, 3, 11, costModel: null).ExUnits);
        Assert.Equal(expected, Evaluator.EvaluateScript(script, vm, 3, 11, costModel: null).ExUnits);
    }

    [Fact]
    public void EvaluateScript_AcceptsBuiltinUnit_AfterApplyingPlutusV3Arguments()
    {
        // The script body is a lambda, not unit; the evaluated result after application is unit.
        byte[] script = Encode(new LambdaTerm<DeBruijn>(new DeBruijn(0), new ConstTerm<DeBruijn>(UnitConstant.Instance)));
        byte[][] cborArguments = [[0x00]];
        PlutusData[] vmArguments = [new PlutusDataInteger(0)];

        EvaluationResult fromCbor = Evaluator.EvaluateScript(script, cborArguments, 3, 11);
        EvaluationResult fromVm = Evaluator.EvaluateScript(script, vmArguments, 3, 11);

        Assert.Equal(fromCbor.ExUnits, fromVm.ExUnits);
        Assert.True(fromVm.ExUnits.Steps > 0);
    }

    [Fact]
    public void EvaluateScript_RefusesAClosureResult_ForPlutusV3()
    {
        // Applying the outer lambda leaves a closure holding its argument.
        LambdaTerm<DeBruijn> script = new(new(0), new LambdaTerm<DeBruijn>(new(0), new VarTerm<DeBruijn>(new(2))));
        PlutusData[] arguments = [new PlutusDataConstr(0, [])];
        _ = Assert.Throws<InvalidReturnValueException>(() => Evaluator.EvaluateScript(Encode(script), arguments, 3, 11));
    }

    private static byte[] Encode(Term<DeBruijn> body) => FlatEncoder.EncodeProgram(new(new(1, 0, 0), body));
}
