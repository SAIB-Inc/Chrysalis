using System.Formats.Cbor;
using System.Runtime.CompilerServices;

namespace Chrysalis.Cbor.Utils;

public static class CborUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CborTag ResolveTag(int index)
    {
        int finalIndex = index > 6 ? 1280 - 7 : 121;
        return (CborTag)(finalIndex + index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReadAndVerifyTag(CborReader reader, int? tag)
    {
        if (reader.PeekState() == CborReaderState.Tag)
        {
            CborTag actualTag = reader.ReadTag();

            if (tag is null || tag < 0) return;

            if ((CborTag)tag != actualTag)
                throw new InvalidOperationException($"Expected tag {tag}, got {actualTag}");

        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteTag(CborWriter writer, int? tag)
    {
        if (tag is not null && tag >= 0)
            writer.WriteTag((CborTag)tag);
    }
}