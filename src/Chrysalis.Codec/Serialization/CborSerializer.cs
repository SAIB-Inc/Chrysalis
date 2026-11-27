using System.Buffers;
using System.Runtime.CompilerServices;
using SAIB.Cbor.Util;

namespace Chrysalis.Codec.Serialization;

/// <summary>Serializes and deserializes CBOR values with typed codec errors.</summary>
/// <remarks>
/// Codec failures through this facade are <see cref="CborDecodeException"/> or
/// <see cref="CborValidationException"/>. A generated Read method called directly can
/// still throw <see cref="SAIB.Cbor.CborException"/>, whose Offset is available since SAIB.Cbor 3.1.0.
/// Cancellation and resource exhaustion are not converted into codec errors.
/// Definite byte strings borrow the input. Keep its backing storage valid and unmodified while using the result, including as a map key.
/// Retaining even a small slice keeps the backing storage alive; call <c>ToArray()</c> to retain the bytes independently.
/// Indefinite byte strings are joined into owned storage.
/// </remarks>
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
    /// An open-generic long field uses typed <see cref="CborCodecCache"/> dispatch without
    /// boxing. Built values used by the fee path preserve their bytes and
    /// bypass that writer path. A thread's first query, a larger scratch span, or a nested
    /// query may allocate; simultaneously active queries always use separate sinks.
    /// </remarks>
    public static long GetEncodedLength<T>(T value) where T : ICborType
    {
        try
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
        catch (Exception error) when (error is not CborDecodeException && CborError.IsDecodeFailure(error))
        {
            throw CborError.Validation(-1, typeof(T).FullName!, "serializable value", error.Message, error);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(T value) where T : ICborType
    {
        try
        {
            if (value.Raw.Length > 0)
            {
                return value.Raw.ToArray();
            }

            ArrayBufferWriter<byte> output = new();
            GenericSerializationUtil.Write(output, value);
            return output.WrittenSpan.ToArray();
        }
        catch (Exception error) when (error is not CborDecodeException && CborError.IsDecodeFailure(error))
        {
            throw CborError.Validation(-1, typeof(T).FullName!, "serializable value", error.Message, error);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlyMemory<byte> SerializeToMemory<T>(T value) where T : ICborType
    {
        try
        {
            if (value.Raw.Length > 0)
            {
                return value.Raw;
            }

            ArrayBufferWriter<byte> output = new();
            GenericSerializationUtil.Write(output, value);
            return output.WrittenMemory;
        }
        catch (Exception error) when (error is not CborDecodeException && CborError.IsDecodeFailure(error))
        {
            throw CborError.Validation(-1, typeof(T).FullName!, "serializable value", error.Message, error);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        try
        {
            T? result = GenericSerializationUtil.Read<T>(data);
            return result ?? throw CborError.Decode(data, 0, typeof(T).FullName!, "non-null decoded value", "null");
        }
        catch (Exception error) when (error is not CborDecodeException && CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, typeof(T).FullName!, CborError.Expected(typeof(T)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try
        {
            T? result = GenericSerializationUtil.ReadAnyWithConsumed<T>(data, out bytesConsumed);
            return result ?? throw CborError.Decode(data, 0, typeof(T).FullName!, "non-null decoded value", "null");
        }
        catch (Exception error) when (error is not CborDecodeException && CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, typeof(T).FullName!, CborError.Expected(typeof(T)));
        }
    }
}
