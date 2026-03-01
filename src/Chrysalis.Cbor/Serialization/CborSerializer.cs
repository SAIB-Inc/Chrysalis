using System.Buffers;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Utils;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Provides methods to serialize and deserialize objects to and from CBOR.
/// </summary>
public static class CborSerializer
{
    /// <summary>Serializes a CborBase-derived object to a byte array.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(T value) where T : CborBase
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Raw is not null)
        {
            return value.Raw.Value.ToArray();
        }

        ArrayBufferWriter<byte> output = new();
        GenericSerializationUtil.Write<T>(output, value);

        return output.WrittenSpan.ToArray();
    }

    /// <summary>Serializes a CborBase-derived object to ReadOnlyMemory without copying when pre-serialized data exists.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlyMemory<byte> SerializeToMemory<T>(T value) where T : CborBase
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Raw is not null)
        {
            return value.Raw.Value;
        }

        ArrayBufferWriter<byte> output = new();
        GenericSerializationUtil.Write<T>(output, value);

        return output.WrittenMemory;
    }

    /// <summary>Deserializes a CBOR byte array into an object of type T.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data) where T : CborBase
    {
        T? result = GenericSerializationUtil.Read<T>(data);
        return result ?? throw new InvalidOperationException("Deserialization failed: result is null.");
    }

    /// <summary>Deserializes a single CBOR value and reports how many bytes were consumed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data, out int bytesConsumed) where T : CborBase
    {
        T? result = GenericSerializationUtil.ReadWithConsumed<T>(data, out bytesConsumed);
        return result ?? throw new InvalidOperationException("Deserialization failed: result is null.");
    }
}
