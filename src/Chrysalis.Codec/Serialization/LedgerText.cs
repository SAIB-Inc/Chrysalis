using System.Runtime.CompilerServices;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Serialization;

internal static class LedgerText
{
    // All currently supported era decoders are below PV12. A future Dijkstra
    // decoder must pass its actual decoder version when scanning these fields.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Skip(ref CborReader reader, ReadOnlyMemory<byte> data, string typePath, int protocolVersion = 11)
    {
        ReadOnlySpan<byte> remaining = reader.Buffer;
        if (remaining.IsEmpty || (remaining[0] is not (>= 0x60 and <= 0x7B) && (protocolVersion < 12 || remaining[0] != 0x7F)))
        {
            throw CborError.Decode(data, data.Length - remaining.Length, typePath, protocolVersion < 12 ? "definite-length text" : "text string");
        }

        reader.SkipDataItem();
    }
}
