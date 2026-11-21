using Chrysalis.Codec.Test;
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

    [Theory]
    [InlineData(3, 11, 1, "0100002324984001", true)] // index 0
    [InlineData(3, 11, 1, "0100002324984009", true)] // depth + 1
    [InlineData(3, 9, 1, "0100002324984009", true)] // depth + 1 at V3 PV 9
    [InlineData(3, 11, 1, "0100002324984005", false)] // bound twin
    [InlineData(3, 9, 1, "0100002324984005", false)] // bound twin at V3 PV 9
    [InlineData(3, 11, 1, "010000232498880101", true)] // depth + 1 under two lambdas
    [InlineData(3, 11, 1, "0100002324988800c1", false)] // index = depth under two lambdas
    [InlineData(3, 11, 1, "01000023249843fffffffffffffffffc05", true)] // index 2^64 - 1
    [InlineData(3, 11, 1, "0100002324989400c1", true)] // under force
    [InlineData(3, 11, 1, "0100002324984c004025", true)] // argument side
    [InlineData(3, 11, 1, "0100002324984c024005", true)] // function side
    [InlineData(1, 5, 2, "0100002232498400c1", true)] // depth + 1 at V1 PV 5
    [InlineData(2, 7, 2, "0100002232498400c1", true)] // depth + 1 at V2 PV 7
    [InlineData(1, 5, 2, "010000223249840081", false)] // bound twin at V1 PV 5
    [InlineData(2, 7, 2, "010000223249840081", false)] // bound twin at V2 PV 7
    [InlineData(1, 7, 0, "010000100001", true)] // delay0
    [InlineData(1, 7, 0, "010000200001", true)] // fun0var0
    [InlineData(1, 7, 0, "01000033220024988001", true)] // const0var0
    [InlineData(1, 7, 0, "010000333573494452888001", true)] // iteLazy0
    [InlineData(1, 7, 0, "0100003335734945288001", true)] // iteStrict0
    public void EvaluateScript_RefusesUnboundVariables(int language, int pv, int argCount, string hex, bool refused)
    {
        byte[] script = Convert.FromHexString(hex);
        byte[][] cbor = [.. Enumerable.Repeat<byte[]>([0xD8, 0x79, 0x80], argCount)];
        PlutusData[] vm = [.. Enumerable.Repeat<PlutusData>(new PlutusDataConstr(0, []), argCount)];
        long[] costs = NodeBudgetTests.Fixture.Models[language - 1];
        if (refused)
        {
            string message = new UnboundVariableException().Message;
            Assert.Equal(message, Assert.IsType<UnboundVariableException>(Record.Exception(() => Evaluator.EvaluateScript(script, cbor, language, pv, costs))).Message);
            Assert.Equal(message, Assert.IsType<UnboundVariableException>(Record.Exception(() => Evaluator.EvaluateScript(script, vm, language, pv, costs))).Message);
            return;
        }
        ExUnitsResult expected = Evaluator.EvaluateScript(script, cbor, language, pv, costs).ExUnits;
        Assert.Equal(expected, Evaluator.EvaluateScript(script, vm, language, pv, costs).ExUnits);
        Assert.True(expected.Steps > 0);
    }

    [Fact]
    public void UnboundVariablesAreFoundInTheNodesOrder()
    {
        byte[][] cbor1 = [[0xD8, 0x79, 0x80]];
        PlutusData[] vm1 = [new PlutusDataConstr(0, [])];
        byte[][] cbor2 = [[0xD8, 0x79, 0x80], [0xD8, 0x79, 0x80]];
        PlutusData[] vm2 = [new PlutusDataConstr(0, []), new PlutusDataConstr(0, [])];
        long[] v1Costs = NodeBudgetTests.Fixture.Models[0];
        long[] v3Costs = NodeBudgetTests.Fixture.Models[2];

        byte[] openV3 = Convert.FromHexString("0100002324984009");
        string message = new UnboundVariableException().Message;
        Assert.Equal(message, Assert.IsType<UnboundVariableException>(Record.Exception(() => Evaluator.EvaluateScript(openV3, cbor1, 3, 11, v3Costs, new ExBudget(0, 0)))).Message);
        Assert.Equal(message, Assert.IsType<UnboundVariableException>(Record.Exception(() => Evaluator.EvaluateScript(openV3, vm1, 3, 11, v3Costs, new ExBudget(0, 0)))).Message);
        Assert.Equal(message, Assert.IsType<UnboundVariableException>(Record.Exception(() => Evaluator.EvaluateScript(openV3, [[0xFF]], 3, 11, v3Costs))).Message);

        byte[] badVersion = Convert.FromHexString("010100223249840241");
        _ = Assert.IsType<EvaluationException>(Record.Exception(() => Evaluator.EvaluateScript(badVersion, cbor2, 1, 10, v1Costs)));
        _ = Assert.IsType<EvaluationException>(Record.Exception(() => Evaluator.EvaluateScript(badVersion, vm2, 1, 10, v1Costs)));

        byte[] badBuiltin = Convert.FromHexString("0100002324984c025f29");
        _ = Assert.IsType<InvalidOperationException>(Record.Exception(() => Evaluator.EvaluateScript(badBuiltin, cbor1, 3, 11, v3Costs)));
        _ = Assert.IsType<InvalidOperationException>(Record.Exception(() => Evaluator.EvaluateScript(badBuiltin, vm1, 3, 11, v3Costs)));
    }

    [Fact]
    public void ScopeCheckAllocatesNothing()
    {
        Term<DeBruijn> closed = Build(1000);
        Term<DeBruijn> open = Build(1001);
        Assert.Equal(0, Measure(closed, expected: true));
        Assert.Equal(0, Measure(open, expected: false));

        static Term<DeBruijn> Build(int leafIndex)
        {
            ConstTerm<DeBruijn> unit = new(UnitConstant.Instance);
            Term<DeBruijn> term = new VarTerm<DeBruijn>(new(leafIndex));
            for (int i = 0; i < 1000; i++)
            {
                term = new ApplyTerm<DeBruijn>(term, unit);
            }
            for (int i = 0; i < 1000; i++)
            {
                term = new LambdaTerm<DeBruijn>(new(0), term);
            }
            return term;
        }

        static long Measure(Term<DeBruijn> term, bool expected)
        {
            bool observed = !expected;
            return AllocationAssert.Measure(
                () => observed = Evaluator.IsClosed(term, 0),
                () =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        observed = Evaluator.IsClosed(term, 0);
                    }
                },
                () => Assert.Equal(expected, observed));
        }
    }

    private static byte[] Encode(Term<DeBruijn> body) => FlatEncoder.EncodeProgram(new(new(1, 0, 0), body));
}
