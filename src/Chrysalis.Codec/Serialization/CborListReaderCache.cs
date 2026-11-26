using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Serialization;

/// <summary>
/// Static generic class that caches a reader delegate for each concrete T.
/// Source-generated code populates Reader for known types, eliminating
/// reflection-based dispatch in CborListEnumerator{T} and CborMap{TKey, TValue}.
/// </summary>
public static class CborListReaderCache<T>
{
#pragma warning disable CA2211 // Non-constant field — intentionally mutable for source-generated registration
    public static ReadWithConsumedHandler<T>? Reader;
#pragma warning restore CA2211
}

/// <summary>
/// Pre-built reader methods for primitive CBOR types.
/// Used by source-generated cache registrations for CborList{T} and CborMap{TKey, TValue}.
/// </summary>
public static class CborPrimitiveReaders
{
    public static int ReadInt32(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); int v = r.ReadInt32(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(int).FullName!, "integer"); }
    }

    public static uint ReadUInt32(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); uint v = r.ReadUInt32(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(uint).FullName!, "unsigned integer"); }
    }

    public static long ReadInt64(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); long v = r.ReadInt64(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(long).FullName!, "integer"); }
    }

    public static ulong ReadUInt64(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); ulong v = r.ReadUInt64(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(ulong).FullName!, "unsigned integer"); }
    }

    public static bool ReadBoolean(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); bool v = r.ReadBoolean(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(bool).FullName!, "boolean"); }
    }

    public static string ReadString(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<string>(data, out bytesConsumed)!;

    public static float ReadSingle(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); float v = r.ReadSingle(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(float).FullName!, "floating point"); }
    }

    public static double ReadDouble(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); double v = r.ReadDouble(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(double).FullName!, "floating point"); }
    }

    public static byte[] ReadByteArray(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadPrimitiveWithConsumed<byte[]>(data, out bytesConsumed)!;

    public static ReadOnlyMemory<byte> ReadByteMemory(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        GenericSerializationUtil.ReadByteMemoryWithConsumed(data, out bytesConsumed);
}
