using System.Runtime.InteropServices;
using Chrysalis.Plutus.Models.Interop;

namespace Chrysalis.Plutus.Interop;

internal static partial class NativeMethods
{
    private const string LibraryName = "plutus_vm_dotnet_rs";

    [LibraryImport(LibraryName, EntryPoint = "eval_tx")]
    public static partial TxEvalResultArray EvalTx(
        [In] byte[] txCborBytes, 
        nuint txCborBytesLength, 
        [In] byte[] utxoCborBytes, 
        nuint utxoCborBytesLength
    );

    [LibraryImport(LibraryName, EntryPoint = "free_eval_results")]
    public static partial void FreeEvalResults(TxEvalResultArray results);
}