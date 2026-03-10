using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.V2.Serialization;

/// <summary>
/// Static generic class that caches a reader delegate for each concrete T.
/// Source-generated code populates Reader for known types, eliminating
/// reflection-based dispatch in CborListEnumerator{T}.
/// </summary>
public static class CborListReaderCache<T>
{
#pragma warning disable CA2211 // Non-constant field — intentionally mutable for source-generated registration
    public static ReadWithConsumedHandler<T>? Reader;
#pragma warning restore CA2211
}

/// <summary>
/// Pre-built reader methods for primitive CBOR types.
/// Used by source-generated cache registrations for ICborMaybeIndefList{T} where T is primitive.
/// </summary>
public static class CborPrimitiveReaders
{
    public static int ReadInt32(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        int value = reader.ReadInt32();
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }

    public static uint ReadUInt32(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        uint value = reader.ReadUInt32();
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }

    public static long ReadInt64(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        long value = reader.ReadInt64();
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }

    public static ulong ReadUInt64(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        ulong value = reader.ReadUInt64();
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }

    public static bool ReadBoolean(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        bool value = reader.ReadBoolean();
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }

    public static string ReadString(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        string value = reader.ReadString()!;
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }

    public static float ReadSingle(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        float value = reader.ReadSingle();
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }

    public static double ReadDouble(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        double value = reader.ReadDouble();
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }

    public static byte[] ReadByteArray(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        byte[] value = reader.ReadByteString().ToArray();
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }

    public static ReadOnlyMemory<byte> ReadByteMemory(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        byte[] value = reader.ReadByteString().ToArray();
        bytesConsumed = data.Length - reader.Buffer.Length;
        return value;
    }
}
