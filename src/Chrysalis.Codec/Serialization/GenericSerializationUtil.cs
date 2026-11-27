using System.Buffers;
using System.Runtime.CompilerServices;
using Chrysalis.Codec.Types;

namespace Chrysalis.Codec.Serialization;

public delegate T ReadHandler<T>(ReadOnlyMemory<byte> data);
public delegate T ReadWithConsumedHandler<T>(ReadOnlyMemory<byte> data, out int bytesConsumed);
public delegate void WriteHandler<T>(IBufferWriter<byte> output, T value);

/// <summary>Dispatches CBOR primitive and generated type serialization.</summary>
/// <remarks>Definite byte strings borrow the input. Keep its backing storage valid and unmodified while using the result, including as a map key.
/// Retaining even a small slice keeps the backing storage alive; call <c>ToArray()</c> to retain the bytes independently. Indefinite byte strings are joined into owned storage.</remarks>
public static class GenericSerializationUtil
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Read<T>(ReadOnlyMemory<byte> data) => ReadAnyWithConsumed<T>(data, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadAnyWithConsumed<T>(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { return CborCodecCache<T>.GetReaderOrThrow(data)(data, out bytesConsumed); }
        catch (Exception error) when (CborError.IsDecodeFailure(error)) { throw CborError.Wrap(error, data, typeof(T).FullName!, CborError.Expected(typeof(T))); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadPrimitive<T>(ReadOnlyMemory<byte> data) => ReadPrimitiveWithConsumed<T>(data, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadPrimitiveWithConsumed<T>(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
        IsPrimitiveType(typeof(T)) ? ReadAnyWithConsumed<T>(data, out bytesConsumed)
            : throw CborError.Decode(data, 0, typeof(T).FullName ?? typeof(T).Name, CborError.Expected(typeof(T)), "unsupported primitive type");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadNonPrimitive<T>(ReadOnlyMemory<byte> data) => ReadAnyWithConsumed<T>(data, out _)!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? Read(ReadOnlyMemory<byte> data, Type type) => ReadAnyWithConsumed(data, type, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadAnyWithConsumed(ReadOnlyMemory<byte> data, Type type, out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(type);
        try { return CborCodecCache.GetBoxedReaderOrThrow(type, data)(data, out bytesConsumed); }
        catch (Exception error) when (CborError.IsDecodeFailure(error)) { throw CborError.Wrap(error, data, type.FullName!, CborError.Expected(type)); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadPrimitive(Type type, ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(type);
        return IsPrimitiveType(type) ? ReadAnyWithConsumed(data, type, out _)
            : throw CborError.Decode(data, 0, type.FullName ?? type.Name, CborError.Expected(type), "unsupported primitive type");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadNonPrimitive(Type type, ReadOnlyMemory<byte> data) => ReadAnyWithConsumed(data, type, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPrimitiveType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(bool) || type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
               type == typeof(double) || type == typeof(decimal) || type == typeof(string) ||
               type == typeof(byte[]) || type == typeof(ReadOnlyMemory<byte>) ||
               type == typeof(CborEncodedValue) || type == typeof(CborAny) || type == typeof(CborLabel);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<T>(IBufferWriter<byte> output, T value)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (value is null && IsPrimitiveType(typeof(T))) { ArgumentNullException.ThrowIfNull(value); }
        WriteNonPrimitive(output, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WritePrimitive<T>(IBufferWriter<byte> output, object? value)
    {
        ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(value);
        if (!IsPrimitiveType(typeof(T))) { throw new CborValidationException(-1, typeof(T).FullName!, "supported primitive type", typeof(T).FullName!); }
        WriteNonPrimitive(output, (T)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteNonPrimitive<T>(IBufferWriter<byte> output, T value)
    {
        ArgumentNullException.ThrowIfNull(output);
        try { CborCodecCache<T>.GetWriterOrThrow(value)(output, value); }
        catch (Exception ex) when (CborError.IsDecodeFailure(ex))
        {
            if (ex is CborValidationException) { throw; }
            throw new CborValidationException(-1, typeof(T).FullName!, "serializable value", ex.Message, ex);
        }
    }
}
