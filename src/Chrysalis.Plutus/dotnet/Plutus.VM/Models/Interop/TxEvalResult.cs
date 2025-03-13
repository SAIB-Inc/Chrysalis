using System.Runtime.InteropServices;

namespace Plutus.VM.Models.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct TxEvalResult
{
    public byte Tag;
    public uint Index;
    public ulong Memory;
    public ulong Steps;
}