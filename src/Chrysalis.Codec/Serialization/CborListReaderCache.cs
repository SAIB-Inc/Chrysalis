namespace Chrysalis.Codec.Serialization;

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
/// Used by source-generated cache registrations for CborList{T} where T is primitive.
/// </summary>
public static class CborPrimitiveReaders
{
    public static int ReadInt32(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<int>(data, out bytesConsumed)!;

    public static uint ReadUInt32(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<uint>(data, out bytesConsumed)!;

    public static long ReadInt64(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<long>(data, out bytesConsumed)!;

    public static ulong ReadUInt64(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<ulong>(data, out bytesConsumed)!;

    public static bool ReadBoolean(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<bool>(data, out bytesConsumed)!;

    public static string ReadString(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<string>(data, out bytesConsumed)!;

    public static float ReadSingle(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<float>(data, out bytesConsumed)!;

    public static double ReadDouble(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<double>(data, out bytesConsumed)!;

    public static byte[] ReadByteArray(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<byte[]>(data, out bytesConsumed)!;

    public static ReadOnlyMemory<byte> ReadByteMemory(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<ReadOnlyMemory<byte>>(data, out bytesConsumed)!;
}
