using Plutus.VM.Models;
using Plutus.VM.Interop;

namespace Plutus.VM.EvalTx;

public class Evaluator
{
    public IReadOnlyList<EvaluationResult> EvaluateTransaction(string txCborHex, string utxosCborHex) =>
        NativeEvaluator.EvaluateTransaction(
            txCborHex, utxosCborHex
        );

}
