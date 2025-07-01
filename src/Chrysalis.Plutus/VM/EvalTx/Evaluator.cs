using System.Runtime.InteropServices;
using Chrysalis.Plutus.VM.Models;
using Chrysalis.Plutus.VM.Models.Interop;
using Chrysalis.Plutus.VM.Models.Enums;
using Chrysalis.Plutus.VM.Interop;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Plutus.VM.EvalTx;


public static class Evaluator
{

    public static IReadOnlyList<EvaluationResult> EvaluateTx(string txCborHex, string utxosCborHex, NetworkType networkType = NetworkType.Preview)
    {
        byte[] txCborBytes = Convert.FromHexString(txCborHex);
        byte[] utxosCborBytes = Convert.FromHexString(utxosCborHex);

        return EvaluateTx(txCborBytes, utxosCborBytes);
    }

    public static IReadOnlyList<EvaluationResult> EvaluateTx(byte[] txCborBytes, byte[] utxosCborBytes, NetworkType networkType = NetworkType.Preview)
    {
        TxEvalResultArray resultArray = NativeMethods.EvalTxRaw(
            txCborBytes,
            (nuint)txCborBytes.Length,
            utxosCborBytes,
            (nuint)utxosCborBytes.Length,
            (uint)networkType
        );

        try
        {

            var results = new List<EvaluationResult>((int)resultArray.Length);
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