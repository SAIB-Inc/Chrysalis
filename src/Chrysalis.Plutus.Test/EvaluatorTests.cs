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
            ("V3 integer, CBOR", Record.Exception(() => Evaluator.EvaluateScript(integerScript, cborArguments))),
            ("V3 integer, VM", Record.Exception(() => Evaluator.EvaluateScript(integerScript, vmArguments))),
            ("V3 non-constant, CBOR", Record.Exception(() => Evaluator.EvaluateScript(delayScript, cborArguments))),
            ("V3 non-constant, VM", Record.Exception(() => Evaluator.EvaluateScript(delayScript, vmArguments))),
            ("V3 data constructor, CBOR", Record.Exception(() => Evaluator.EvaluateScript(dataScript, cborArguments))),
            ("V3 data constructor, VM", Record.Exception(() => Evaluator.EvaluateScript(dataScript, vmArguments))),
            ("Later language, CBOR", Record.Exception(() => Evaluator.EvaluateScript(integerScript, cborArguments, plutusVersion: 4))),
            ("Later language, VM", Record.Exception(() => Evaluator.EvaluateScript(integerScript, vmArguments, plutusVersion: 4)))
        }, result => Assert.IsType<InvalidReturnValueException>(result.Error));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EvaluateScript_RefusesPlutusV1AndV2(int version)
    {
        byte[] script = Encode(IntegerOne);
        byte[][] cborArguments = [];
        PlutusData[] vmArguments = [];
        long[] costs = NodeBudgetTests.Fixture.Models[version - 1];
        foreach (IReadOnlyList<long>? array in new IReadOnlyList<long>?[] { null, costs })
        {
            _ = Assert.Throws<NotSupportedException>(() => Evaluator.EvaluateScript(script, cborArguments, array, version));
            _ = Assert.Throws<NotSupportedException>(() => Evaluator.EvaluateScript(script, vmArguments, array, version));
        }
        Assert.True(Evaluator.Run(IntegerOne, CostModelLoader.FromFlat(costs, version), version, null).ExUnits.Steps > 0);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void EvaluateScript_UsesTheSuppliedArray(int version)
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
            Assert.Equal(expected, Evaluator.EvaluateScript(script, cborArguments, costs, version).ExUnits);
            Assert.Equal(expected, Evaluator.EvaluateScript(script, vmArguments, costs, version).ExUnits);
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
        ExUnitsResult expected = Evaluator.EvaluateScript(script, vm, DefaultCosts.LatestV3).ExUnits;
        Assert.NotEqual(expected, Evaluator.Run(term, CostModel.Default, 3, null).ExUnits);
        Assert.Equal(expected, Evaluator.EvaluateScript(script, cbor, costModel: null).ExUnits);
        Assert.Equal(expected, Evaluator.EvaluateScript(script, vm, costModel: null).ExUnits);
    }

    [Fact]
    public void EvaluateScript_AcceptsBuiltinUnit_AfterApplyingPlutusV3Arguments()
    {
        // The script body is a lambda, not unit; the evaluated result after application is unit.
        byte[] script = Encode(new LambdaTerm<DeBruijn>(new DeBruijn(0), new ConstTerm<DeBruijn>(UnitConstant.Instance)));
        byte[][] cborArguments = [[0x00]];
        PlutusData[] vmArguments = [new PlutusDataInteger(0)];

        EvaluationResult fromCbor = Evaluator.EvaluateScript(script, cborArguments);
        EvaluationResult fromVm = Evaluator.EvaluateScript(script, vmArguments);

        Assert.Equal(fromCbor.ExUnits, fromVm.ExUnits);
        Assert.True(fromVm.ExUnits.Steps > 0);
    }

    [Fact]
    public void EvaluateScript_RefusesAClosureResult_ForPlutusV3()
    {
        // Applying the outer lambda leaves a closure holding its argument.
        LambdaTerm<DeBruijn> script = new(new(0), new LambdaTerm<DeBruijn>(new(0), new VarTerm<DeBruijn>(new(2))));
        PlutusData[] arguments = [new PlutusDataConstr(0, [])];
        _ = Assert.Throws<InvalidReturnValueException>(() => Evaluator.EvaluateScript(Encode(script), arguments));
    }

    private static byte[] Encode(Term<DeBruijn> body) => FlatEncoder.EncodeProgram(new(new(1, 0, 0), body));
}
