using Chrysalis.Plutus.Models;
using Chrysalis.Plutus.Interop;

namespace Chrysalis.Plutus.Eval;

public class Evaluator
{
    public IReadOnlyList<EvaluationResult> EvaluateTransaction(string txCborHex, string utxosCborHex) =>
        NativeEvaluator.EvaluateTransaction(
            txCborHex, utxosCborHex
        );

}
