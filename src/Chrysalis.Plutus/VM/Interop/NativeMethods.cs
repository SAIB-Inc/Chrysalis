using System.Reflection;
using System.Runtime.InteropServices;
using Chrysalis.Plutus.VM.Models.Interop;

namespace Chrysalis.Plutus.VM.Interop;

internal static partial class NativeMethods
{
    private const string LibraryName = "plutus_vm_dotnet_rs";

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveLibrary);
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return nint.Zero;
        }

        // Try default resolution first
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out nint handle))
        {
            return handle;
        }

        // Fallback: probe runtimes/<rid>/native/ relative to the app base directory
        string? appBase = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(appBase))
        {
            string rid = RuntimeInformation.RuntimeIdentifier;
            string ext = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "dylib" : "so";
            string candidate = Path.Combine(appBase, "runtimes", rid, "native", $"lib{libraryName}.{ext}");

            if (NativeLibrary.TryLoad(candidate, out handle))
            {
                return handle;
            }

            // Also try flat in the app directory
            candidate = Path.Combine(appBase, $"lib{libraryName}.{ext}");
            if (NativeLibrary.TryLoad(candidate, out handle))
            {
                return handle;
            }
        }

        return nint.Zero;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(LibraryName, EntryPoint = "eval_tx")]
    public static partial TxEvalResultArray EvalTxRaw(
        [In] byte[] txCborBytes,
        nuint txCborBytesLength,
        [In] byte[] utxoCborBytes,
        nuint utxoCborBytesLength,
        uint networkType
    );

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(LibraryName, EntryPoint = "free_eval_results")]
    public static partial void FreeEvalResults(TxEvalResultArray results);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(LibraryName, EntryPoint = "apply_params_to_script_raw")]
    public static unsafe partial byte* ApplyParamsToScriptRaw(
        [In] byte[] scriptCborBytes,
        nuint scriptCborBytesLength,
        [In] byte[] paramsCborBytes,
        nuint paramsCborBytesLength,
        out nuint outLength
    );

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(LibraryName, EntryPoint = "free_script_bytes")]
    public static partial void FreeScriptBytes(IntPtr ptr, nuint len);
}
