using System.Runtime.InteropServices;

namespace Plutus.VM.Models.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct TxEvalResultArray
{
    public IntPtr Ptr;

    public nuint Length;

    public bool IsEmpty => Ptr == IntPtr.Zero || Length == 0;
}
