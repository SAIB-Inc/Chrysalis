
using System.Runtime.InteropServices;
using Chrysalis.Plutus.Models;
using Chrysalis.Plutus.Models.Enums;
using Chrysalis.Plutus.Models.Interop;
using Chrysalis.Plutus.Utils;

namespace Chrysalis.Plutus.Interop;

internal static class NativeEvaluator
{

    internal static IReadOnlyList<EvaluationResult> EvaluateTransaction(string txCborHex, string utxosCborHex)
    {
        ArgumentException.ThrowIfNullOrEmpty(txCborHex, nameof(txCborHex));
        ArgumentException.ThrowIfNullOrEmpty(utxosCborHex, nameof(utxosCborHex));

        try
        {

            byte[] transactionCborBytes = Converter.HexToBytes(txCborHex);
            byte[] utxosCborBytes = Converter.HexToBytes(utxosCborHex);

            var nativeResults = EvaluateNative(transactionCborBytes, utxosCborBytes);

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

    internal static List<TxEvalResult> EvaluateNative(byte[] transactionCborBytes, byte[] utxosCborBytes)
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