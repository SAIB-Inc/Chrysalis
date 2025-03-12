using System.Runtime.InteropServices;


namespace PlutusVM.Eval;

public struct TxEvalResult
{
    public byte Tag;  // Assuming RedeemerTag is represented as a byte
    public uint Index;
    public ulong Memory;  // Assuming ExUnits has memory field
    public ulong Steps;   // Assuming ExUnits has steps field
}

[StructLayout(LayoutKind.Sequential)]
public struct TxEvalResultArray
{
    public IntPtr Ptr;
    public nuint Length;

    public bool IsEmpty => Ptr == IntPtr.Zero || Length == 0;
}


// Importing native library using the new .NET 9 NativeLibrary methods
// and modern C# features like file-scoped namespaces and top-level statements
public static partial class Eval
{


    [LibraryImport("plutus_vm_dotnet_rs", EntryPoint = "eval_tx")]
    private static partial TxEvalResultArray EvalTx([In] byte[] txCborBytes, nuint txCborBytesLength, [In] byte[] utxoCborBytes, nuint utxoCborBytesLength);

    [LibraryImport("plutus_vm_dotnet_rs")]
    private static partial void free_tx_results(TxEvalResultArray results);

    public static List<TxEvalResult> EvaluateTransaction(string txCborHex, string rawUtxosCborHex)
    {
        byte[] cborBytes = HexToBytes(txCborHex);
        byte[] utxoBytes = HexToBytes(rawUtxosCborHex);
        return EvaluateTransactionBytes(cborBytes, utxoBytes);
    }
    public static List<TxEvalResult> EvaluateTransactionBytes(byte[] txCborBytes, byte[] utxoCborBytes)


    {
        TxEvalResultArray resultArray = EvalTx(txCborBytes, (nuint)txCborBytes.Length, utxoCborBytes, (nuint)utxoCborBytes.Length);


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
            free_tx_results(resultArray);
        }
    }

    private static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return [];

        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }
}



