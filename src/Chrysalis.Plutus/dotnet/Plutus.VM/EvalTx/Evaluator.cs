using Plutus.VM.Models;
using Plutus.VM.Interop;

namespace Plutus.VM.EvalTx;

public class Evaluator
{
    public IReadOnlyList<EvaluationResult> EvaluateTx(string txCborHex, string utxosCborHex) =>
        NativeEvaluator.EvaluateTx(
            txCborHex, utxosCborHex
        );

}
