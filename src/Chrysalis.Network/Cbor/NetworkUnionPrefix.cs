using System.Buffers.Binary;

namespace Chrysalis.Network.Cbor;

// A non-throwing look at a CBOR head. SAIB 3.1 has no TryRead for array or integer heads.
// The selected concrete decoder still reads the original memory, not the advanced cursor.
internal static class NetworkUnionPrefix
{
    internal static bool TrySkipTag(ref ReadOnlySpan<byte> data) =>
        data.IsEmpty || data[0] >> 5 != 6 || TryHead(ref data, out _, out _);

    internal static bool TryHead(ref ReadOnlySpan<byte> data, out byte initial, out ulong argument)
    {
        initial = data.IsEmpty ? (byte)0xFF : data[0];
        int info = initial & 31;
        int length = info switch { < 24 => 0, < 28 => 1 << (info - 24), _ => -1 };
        argument = 0;
        if (length < 0 || data.Length <= length)
        {
            return false;
        }
        argument = length switch
        {
            0 => (uint)info,
            1 => data[1],
            2 => BinaryPrimitives.ReadUInt16BigEndian(data[1..]),
            4 => BinaryPrimitives.ReadUInt32BigEndian(data[1..]),
            _ => BinaryPrimitives.ReadUInt64BigEndian(data[1..])
        };
        data = data[(1 + length)..];
        return true;
    }
}
