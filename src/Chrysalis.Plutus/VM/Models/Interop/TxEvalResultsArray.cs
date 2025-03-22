using System.Runtime.InteropServices;

namespace Chrysalis.Plutus.VM.Models.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct TxEvalResultArray
{
    public IntPtr Ptr;

    public nuint Length;

    public readonly bool IsEmpty => Ptr == IntPtr.Zero || Length == 0;

}
