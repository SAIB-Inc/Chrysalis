using Chrysalis.Plutus.Flat;
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

    [Fact]
    public void EvaluateScript_AcceptsTheSameIntegerResult_ForPlutusV1AndV2()
    {
        byte[] script = Encode(IntegerOne);
        byte[][] cborArguments = [];
        PlutusData[] vmArguments = [];
        foreach (int version in new[] { 1, 2 })
        {
            EvaluationResult fromCbor = Evaluator.EvaluateScript(script, cborArguments, plutusVersion: version);
            EvaluationResult fromVm = Evaluator.EvaluateScript(script, vmArguments, plutusVersion: version);

            Assert.Equal(fromCbor.ExUnits, fromVm.ExUnits);
        }
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

    private static byte[] Encode(Term<DeBruijn> body) => FlatEncoder.EncodeProgram(new(new(1, 0, 0), body));
}
