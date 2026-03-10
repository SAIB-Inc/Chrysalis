using System.Buffers;
using System.Runtime.CompilerServices;

namespace Chrysalis.Codec.V2.Serialization;

public static class CborSerializer
{
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
