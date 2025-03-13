using System.Runtime.InteropServices;
using Chrysalis.Plutus.VM.Models;
using Chrysalis.Plutus.VM.Models.Interop;
using Chrysalis.Plutus.VM.Models.Enums;
using Chrysalis.Plutus.VM.Interop;

namespace Chrysalis.Plutus.VM.EvalTx;


public static class Evaluator
{
    public static IReadOnlyList<EvaluationResult> EvaluateTx(string txCborHex, string utxosCborHex)
    {


        byte[] txBytes = Convert.FromHexString(txCborHex);
        byte[] utxoBytes = Convert.FromHexString(utxosCborHex);

        TxEvalResultArray resultArray = NativeMethods.EvalTxRaw(
            txBytes,
            (nuint)txBytes.Length,
            utxoBytes,
            (nuint)utxoBytes.Length
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