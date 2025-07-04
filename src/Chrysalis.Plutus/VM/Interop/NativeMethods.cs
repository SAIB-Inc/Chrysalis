using System.Runtime.InteropServices;
using Chrysalis.Plutus.VM.Models.Interop;

namespace Chrysalis.Plutus.VM.Interop;

internal static partial class NativeMethods
{
    private const string LibraryName = "plutus_vm_dotnet_rs";

    [LibraryImport(LibraryName, EntryPoint = "eval_tx")]
    public static partial TxEvalResultArray EvalTxRaw(
        [In] byte[] txCborBytes,
        nuint txCborBytesLength,
        [In] byte[] utxoCborBytes,
        nuint utxoCborBytesLength,
        uint networkType
    );

    [LibraryImport(LibraryName, EntryPoint = "free_eval_results")]
    public static partial void FreeEvalResults(TxEvalResultArray results);
}