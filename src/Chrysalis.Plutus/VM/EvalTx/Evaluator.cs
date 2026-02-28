using System.Runtime.InteropServices;
using Chrysalis.Plutus.VM.Models;
using Chrysalis.Plutus.VM.Models.Interop;
using Chrysalis.Plutus.VM.Models.Enums;
using Chrysalis.Plutus.VM.Interop;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Plutus.VM.EvalTx;

/// <summary>
/// Provides methods to evaluate Plutus scripts within Cardano transactions.
/// </summary>
public static class Evaluator
{
    /// <summary>
    /// Evaluates a transaction's Plutus scripts using hex-encoded CBOR representations.
    /// </summary>
    /// <param name="txCborHex">The hex-encoded CBOR of the transaction.</param>
    /// <param name="utxosCborHex">The hex-encoded CBOR of the UTxO set.</param>
    /// <param name="networkType">The Cardano network type to use for evaluation.</param>
    /// <returns>A read-only list of evaluation results for each script in the transaction.</returns>
    public static IReadOnlyList<EvaluationResult> EvaluateTx(string txCborHex, string utxosCborHex, NetworkType networkType = NetworkType.Preview)
    {
        byte[] txCborBytes = Convert.FromHexString(txCborHex);
        byte[] utxosCborBytes = Convert.FromHexString(utxosCborHex);

        return EvaluateTx(txCborBytes, utxosCborBytes, networkType);
    }

    /// <summary>
    /// Evaluates a transaction's Plutus scripts using raw CBOR byte arrays.
    /// </summary>
    /// <param name="txCborBytes">The CBOR-encoded transaction bytes.</param>
    /// <param name="utxosCborBytes">The CBOR-encoded UTxO set bytes.</param>
    /// <param name="networkType">The Cardano network type to use for evaluation.</param>
    /// <returns>A read-only list of evaluation results for each script in the transaction.</returns>
    public static IReadOnlyList<EvaluationResult> EvaluateTx(byte[] txCborBytes, byte[] utxosCborBytes, NetworkType networkType = NetworkType.Preview)
    {
        ArgumentNullException.ThrowIfNull(txCborBytes);
        ArgumentNullException.ThrowIfNull(utxosCborBytes);

        TxEvalResultArray resultArray = NativeMethods.EvalTxRaw(
            txCborBytes,
            (nuint)txCborBytes.Length,
            utxosCborBytes,
            (nuint)utxosCborBytes.Length,
            (uint)networkType
        );

        try
        {
            List<EvaluationResult> results = new((int)resultArray.Length);
            int structSize = Marshal.SizeOf<TxEvalResult>();

            for (int i = 0; i < (int)resultArray.Length; i++)
            {
                IntPtr currentPtr = IntPtr.Add(resultArray.Ptr, i * structSize);
                TxEvalResult result = Marshal.PtrToStructure<TxEvalResult>(currentPtr);
                results.Add(new EvaluationResult(
                    (RedeemerTag)result.Tag,
                    result.Index,
                    new ExUnits(result.Memory, result.Steps)
                ));
            }

            return results;
        }
        finally
        {
            NativeMethods.FreeEvalResults(resultArray);
        }
    }
}
