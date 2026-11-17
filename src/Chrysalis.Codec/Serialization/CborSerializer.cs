using System.Buffers;
using System.Runtime.CompilerServices;
using SAIB.Cbor.Util;

namespace Chrysalis.Codec.Serialization;

public static class CborSerializer
{
    [ThreadStatic]
    private static CountingBufferWriter? _lengthOutput;

    /// <summary>Gets the encoded byte length without retaining a new encoding.</summary>
    /// <remarks>
    /// Preserved bytes are counted directly, without invoking the writer or its callbacks.
    /// Otherwise this runs the generated writer once, including any serialization callbacks;
    /// querying the length and then serializing runs those callbacks twice. Use this path for
    /// callback-bearing values only when that is acceptable. Non-nested queries add no
    /// allocation beyond the value's own writer once this thread's sink and dispatch are warm.
    /// An open-generic long field, for example, boxes 24 B per query through
    /// GenericSerializationUtil. Built values used by the fee path preserve their bytes and
    /// bypass that writer path. A thread's first query, a larger scratch span, or a nested
    /// query may allocate; simultaneously active queries always use separate sinks.
    /// </remarks>
    public static long GetEncodedLength<T>(T value) where T : ICborType
    {
        if (value.Raw.Length > 0)
        {
            return value.Raw.Length;
        }

        CountingBufferWriter output = _lengthOutput ?? new();
        _lengthOutput = null;
        try
        {
            output.Reset();
            GenericSerializationUtil.Write(output, value);
            return output.WrittenCount;
        }
        finally
        {
            _lengthOutput = output;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(T value) where T : ICborType
    {
        if (value.Raw.Length > 0)
        {
            return value.Raw.ToArray();
        }

        ArrayBufferWriter<byte> output = new();
        GenericSerializationUtil.Write(output, value);
        return output.WrittenSpan.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlyMemory<byte> SerializeToMemory<T>(T value) where T : ICborType
    {
        if (value.Raw.Length > 0)
        {
            return value.Raw;
        }

        ArrayBufferWriter<byte> output = new();
        GenericSerializationUtil.Write(output, value);
        return output.WrittenMemory;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        T? result = GenericSerializationUtil.Read<T>(data);
        return result ?? throw new InvalidOperationException("Deserialization failed: result is null.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        T? result = GenericSerializationUtil.ReadAnyWithConsumed<T>(data, out bytesConsumed);
        return result ?? throw new InvalidOperationException("Deserialization failed: result is null.");
    }
}
