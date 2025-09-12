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

    [LibraryImport(LibraryName, EntryPoint = "apply_params_to_script_raw")]
    public static unsafe partial byte* ApplyParamsToScriptRaw(
        [In] byte[] scriptCborBytes,
        nuint scriptCborBytesLength,
        [In] byte[] paramsCborBytes,
        nuint paramsCborBytesLength,
        out nuint outLength
    );

    [LibraryImport(LibraryName, EntryPoint = "free_script_bytes")]
    public static partial void FreeScriptBytes(IntPtr ptr, nuint len);
}