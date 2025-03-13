
using System.Runtime.InteropServices;
using Plutus.VM.Models;
using Plutus.VM.Models.Enums;
using Plutus.VM.Models.Interop;

namespace Plutus.VM.Interop;

internal static class NativeEvaluator
{

    internal static IReadOnlyList<EvaluationResult> EvaluateTx(string txCborHex, string utxosCborHex)
    {
        ArgumentException.ThrowIfNullOrEmpty(txCborHex, nameof(txCborHex));
        ArgumentException.ThrowIfNullOrEmpty(utxosCborHex, nameof(utxosCborHex));

        try
        {

            byte[] transactionCborBytes = Convert.FromHexString(txCborHex);
            byte[] utxosCborBytes = Convert.FromHexString(utxosCborHex);

            var nativeResults = EvaluateRaw(transactionCborBytes, utxosCborBytes);

            return [.. nativeResults
                .Select(native => new EvaluationResult(
                    (RedeemerTag)native.Tag,
                    native.Index,
                    new ExUnits(native.Memory, native.Steps)))];
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to evaluate transaction", ex);
        }
    }

    internal static List<TxEvalResult> EvaluateRaw(byte[] transactionCborBytes, byte[] utxosCborBytes)
    {
        TxEvalResultArray resultArray = NativeMethods.EvalTx(
            transactionCborBytes,
            (nuint)transactionCborBytes.Length,
            utxosCborBytes,
            (nuint)utxosCborBytes.Length
        );

        if (resultArray.IsEmpty)
        {
            return [];
        }

        try
        {

            var results = new List<TxEvalResult>((int)resultArray.Length);
            int structSize = Marshal.SizeOf<TxEvalResult>();

            for (int i = 0; i < (int)resultArray.Length; i++)
            {
                IntPtr currentPtr = IntPtr.Add(resultArray.Ptr, i * structSize);
                TxEvalResult result = Marshal.PtrToStructure<TxEvalResult>(currentPtr);
                results.Add(result);
            }

            return results;
        }
        finally
        {
            NativeMethods.FreeEvalResults(resultArray);
        }
    }

}