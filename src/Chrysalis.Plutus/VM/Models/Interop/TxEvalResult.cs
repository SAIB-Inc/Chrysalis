using System.Runtime.InteropServices;

namespace Chrysalis.Plutus.VM.Models.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct TxEvalResult
{
    public byte Tag;
    public uint Index;
    public ulong Memory;
    public ulong Steps;
}