using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Chrysalis.Codec.Types;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Serialization;

public delegate T ReadHandler<T>(ReadOnlyMemory<byte> data);
public delegate T ReadWithConsumedHandler<T>(ReadOnlyMemory<byte> data, out int bytesConsumed);
public delegate void WriteHandler<T>(IBufferWriter<byte> output, T value);

/// <summary>Dispatches CBOR primitive and generated type serialization.</summary>
/// <remarks>Definite byte strings borrow the input. Keep its backing storage valid and unmodified while using the result, including as a map key.
/// Retaining even a small slice keeps the backing storage alive; call <c>ToArray()</c> to retain the bytes independently. Indefinite byte strings are joined into owned storage.</remarks>
public static class GenericSerializationUtil
{
    private static readonly ConcurrentDictionary<Type, Delegate> ReadMethodCache = [];
    private static readonly ConcurrentDictionary<Type, Delegate> ReadWithConsumedMethodCache = [];
    private static readonly ConcurrentDictionary<Type, Delegate> WriteMethodCache = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Read<T>(ReadOnlyMemory<byte> data) => IsPrimitiveType(typeof(T)) ? ReadPrimitive<T>(data) : ReadNonPrimitive<T>(data);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadAnyWithConsumed<T>(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try
        {
            if (IsPrimitiveType(typeof(T)))
            {
                return ReadPrimitiveWithConsumed<T>(data, out bytesConsumed);
            }

            if (default(T) is null && typeof(T).IsValueType)
            {
                return (T?)ReadNonPrimitiveWithConsumed(typeof(T), data, out bytesConsumed);
            }

            if (!ReadWithConsumedMethodCache.TryGetValue(typeof(T), out Delegate? readDelegate))
            {
                MethodInfo readMethod = typeof(T).GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(ReadOnlyMemory<byte>), typeof(int).MakeByRefType()], null)
                    ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Read(ReadOnlyMemory<byte>, out int) method.");

                readDelegate = Delegate.CreateDelegate(typeof(ReadWithConsumedHandler<T>), readMethod);
                ReadWithConsumedMethodCache[typeof(T)] = readDelegate;
            }

            return ((ReadWithConsumedHandler<T>)readDelegate)(data, out bytesConsumed);
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, typeof(T).FullName!, CborError.Expected(typeof(T)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadPrimitiveWithConsumed<T>(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try
        {
            CborReader reader = new(data.Span);
            Type type = typeof(T);

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                type = Nullable.GetUnderlyingType(type)!;
            }

            T? result = true switch
            {
                _ when type == typeof(bool) => (T)(object)reader.ReadBoolean(),
                _ when type == typeof(int) => (T)(object)reader.ReadInt32(),
                _ when type == typeof(uint) => (T)(object)reader.ReadUInt32(),
                _ when type == typeof(long) => (T)(object)reader.ReadInt64(),
                _ when type == typeof(ulong) => (T)(object)reader.ReadUInt64(),
                _ when type == typeof(float) => (T)(object)reader.ReadSingle(),
                _ when type == typeof(double) => (T)(object)reader.ReadDouble(),
                _ when type == typeof(decimal) => (T)(object)reader.ReadDecimal(),
                _ when type == typeof(string) => (T)(object)reader.ReadString()!,
                _ when type == typeof(byte[]) => (T)(object)ReadByteArray(ref reader),
                _ when type == typeof(ReadOnlyMemory<byte>) => (T)(object)ReadByteMemory(ref reader, data),
                _ when type == typeof(CborEncodedValue) => ReadCborEncodedValueAsT<T>(ref reader, data),
                _ when type == typeof(CborAny) => (T)(object)ReadCborAny(ref reader, data),
                _ when type == typeof(CborLabel) => (T)(object)ReadCborLabel(ref reader),
                _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
            };

            bytesConsumed = data.Length - reader.Buffer.Length;
            return result;
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, typeof(T).FullName!, CborError.Expected(typeof(T)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? Read(ReadOnlyMemory<byte> data, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        try
        {
            return IsPrimitiveType(type) ? ReadPrimitive(type, data) : ReadNonPrimitive(type, data);
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, type.FullName!, CborError.Expected(type));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadAnyWithConsumed(ReadOnlyMemory<byte> data, Type type, out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(type);
        try
        {
            return IsPrimitiveType(type)
                ? ReadPrimitiveWithConsumed(type, data, out bytesConsumed)
                : ReadNonPrimitiveWithConsumed(type, data, out bytesConsumed);
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, type.FullName!, CborError.Expected(type));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPrimitiveType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = Nullable.GetUnderlyingType(type)!;
        }

        return type == typeof(bool) || type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
               type == typeof(double) || type == typeof(decimal) || type == typeof(string) ||
               type == typeof(byte[]) || type == typeof(ReadOnlyMemory<byte>) ||
               type == typeof(CborEncodedValue) || type == typeof(CborAny) || type == typeof(CborLabel);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadPrimitive<T>(ReadOnlyMemory<byte> data)
    {
        try
        {
            CborReader reader = new(data.Span);
            Type type = typeof(T);

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                type = Nullable.GetUnderlyingType(type)!;
            }

            return true switch
            {
                _ when type == typeof(bool) => (T)(object)reader.ReadBoolean(),
                _ when type == typeof(int) => (T)(object)reader.ReadInt32(),
                _ when type == typeof(uint) => (T)(object)reader.ReadUInt32(),
                _ when type == typeof(long) => (T)(object)reader.ReadInt64(),
                _ when type == typeof(ulong) => (T)(object)reader.ReadUInt64(),
                _ when type == typeof(float) => (T)(object)reader.ReadSingle(),
                _ when type == typeof(double) => (T)(object)reader.ReadDouble(),
                _ when type == typeof(decimal) => (T)(object)reader.ReadDecimal(),
                _ when type == typeof(string) => (T)(object)reader.ReadString()!,
                _ when type == typeof(byte[]) => (T)(object)ReadByteArray(ref reader),
                _ when type == typeof(ReadOnlyMemory<byte>) => (T)(object)ReadByteMemory(ref reader, data),
                _ when type == typeof(CborEncodedValue) => (T)(object)CborEncodedValue.Read(data),
                _ when type == typeof(CborAny) => (T)(object)ReadCborAny(ref reader, data),
                _ when type == typeof(CborLabel) => (T)(object)ReadCborLabel(ref reader),
                _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
            };
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, typeof(T).FullName!, CborError.Expected(typeof(T)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadPrimitive(Type type, ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(type);
        try
        {

            if (Nullable.GetUnderlyingType(type) != null)
            {
                return ReadPrimitive(Nullable.GetUnderlyingType(type)!, data);
            }

            CborReader reader = new(data.Span);

            return true switch
            {
                _ when type == typeof(bool) => reader.ReadBoolean(),
                _ when type == typeof(int) => reader.ReadInt32(),
                _ when type == typeof(uint) => reader.ReadUInt32(),
                _ when type == typeof(long) => reader.ReadInt64(),
                _ when type == typeof(ulong) => reader.ReadUInt64(),
                _ when type == typeof(float) => reader.ReadSingle(),
                _ when type == typeof(double) => reader.ReadDouble(),
                _ when type == typeof(decimal) => reader.ReadDecimal(),
                _ when type == typeof(string) => reader.ReadString()!,
                _ when type == typeof(byte[]) => ReadByteArray(ref reader),
                _ when type == typeof(ReadOnlyMemory<byte>) => ReadByteMemory(ref reader, data),
                _ when type == typeof(CborEncodedValue) => CborEncodedValue.Read(data),
                _ when type == typeof(CborAny) => ReadCborAny(ref reader, data),
                _ when type == typeof(CborLabel) => ReadCborLabel(ref reader),
                _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
            };
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, type.FullName!, CborError.Expected(type));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? ReadPrimitiveWithConsumed(Type type, ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try
        {
            CborReader reader = new(data.Span);

            if (Nullable.GetUnderlyingType(type) != null)
            {
                type = Nullable.GetUnderlyingType(type)!;
            }

            if (type == typeof(CborEncodedValue))
            {
                int pos = data.Length - reader.Buffer.Length;
                CborEncodedValue cev = CborEncodedValue.Read(data[pos..], out int consumed);
                bytesConsumed = pos + consumed;
                return cev;
            }

            object? result = true switch
            {
                _ when type == typeof(bool) => reader.ReadBoolean(),
                _ when type == typeof(int) => reader.ReadInt32(),
                _ when type == typeof(uint) => reader.ReadUInt32(),
                _ when type == typeof(long) => reader.ReadInt64(),
                _ when type == typeof(ulong) => reader.ReadUInt64(),
                _ when type == typeof(float) => reader.ReadSingle(),
                _ when type == typeof(double) => reader.ReadDouble(),
                _ when type == typeof(decimal) => reader.ReadDecimal(),
                _ when type == typeof(string) => reader.ReadString()!,
                _ when type == typeof(byte[]) => ReadByteArray(ref reader),
                _ when type == typeof(ReadOnlyMemory<byte>) => ReadByteMemory(ref reader, data),
                _ when type == typeof(CborAny) => ReadCborAny(ref reader, data),
                _ when type == typeof(CborLabel) => ReadCborLabel(ref reader),
                _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
            };
            bytesConsumed = data.Length - reader.Buffer.Length;
            return result;
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, type.FullName!, CborError.Expected(type));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? ReadNonPrimitiveWithConsumed(Type type, ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (Nullable.GetUnderlyingType(type) != null)
        {
            type = Nullable.GetUnderlyingType(type)!;
        }

        MethodInfo readMethod = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(ReadOnlyMemory<byte>), typeof(int).MakeByRefType()], null)
            ?? throw new NotSupportedException($"Type {type.FullName} does not have a valid static Read(ReadOnlyMemory<byte>, out int) method.");

        object?[] args = [data, 0];
        try
        {
            object? result = readMethod.Invoke(null, args);
            bytesConsumed = (int)args[1]!;
            return result;
        }
        catch (Exception exception) when (CborError.IsDecodeFailure(exception))
        {
            throw CborError.Wrap(exception, data, type.FullName!, CborError.Expected(type));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CborLabel ReadCborLabel(ref CborReader reader)
    {
        CborDataItemType itemType = reader.GetCurrentDataItemType();
#pragma warning disable IDE0072 // Populate switch — only Unsigned/Signed/String are valid for CborLabel
        return itemType switch
        {
            CborDataItemType.Unsigned or CborDataItemType.Signed => new CborLabel(reader.ReadInt64()),
            CborDataItemType.String => new CborLabel(reader.ReadString()!),
            _ => throw new CborDecodeException(0, "CborLabel", "integer or text string", itemType.ToString())
        };
#pragma warning restore IDE0072
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CborAny ReadCborAny(ref CborReader reader, ReadOnlyMemory<byte> data)
    {
        int pos = data.Length - reader.Buffer.Length;
        if (!reader.Buffer.IsEmpty && reader.Buffer[0] == 0xFF) { throw CborError.Decode(data, pos, typeof(CborAny).FullName!, "valid CBOR data item", "unexpected break"); }
        _ = reader.ReadDataItem();
        return new CborAny(data[pos..(data.Length - reader.Buffer.Length)]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ReadCborEncodedValueAsT<T>(ref CborReader reader, ReadOnlyMemory<byte> data)
    {
        int pos = data.Length - reader.Buffer.Length;
        CborEncodedValue cev = CborEncodedValue.Read(data[pos..], out int consumed);
        reader = new(data.Span[(pos + consumed)..]);
        return (T)(object)cev;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] ReadByteArray(ref CborReader reader) => reader.ReadByteStringToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlyMemory<byte> ReadByteMemory(ref CborReader reader, ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> content = reader.Buffer;
        if (!content.IsEmpty && (content[0] >> 5) == 6)
        {
            CborReader probe = reader;
            _ = probe.TryReadSemanticTag(out _);
            content = probe.Buffer;
        }
        if (!content.IsEmpty && content[0] == 0x5F)
        {
            return reader.ReadByteStringToArray();
        }
        CborReader sequenceReader = new(new ReadOnlySequence<byte>(data));
        ReadOnlyMemory<byte> result = sequenceReader.ReadByteStringSequence().First;
        reader = new(data.Span[sequenceReader.GetBookmark().currentPos..]);
        return result;
    }

    internal static ReadOnlyMemory<byte> ReadByteMemoryWithConsumed(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try
        {
            CborReader reader = new(data.Span);
            ReadOnlyMemory<byte> value = ReadByteMemory(ref reader, data);
            bytesConsumed = data.Length - reader.Buffer.Length;
            return value;
        }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, typeof(ReadOnlyMemory<byte>).FullName!, "byte string");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadNonPrimitive<T>(ReadOnlyMemory<byte> data)
    {
        try
        {
            if (default(T) is null && typeof(T).IsValueType)
            {
                return (T)ReadNonPrimitive(typeof(T), data)!;
            }

            if (!ReadMethodCache.TryGetValue(typeof(T), out Delegate? readDelegate))
            {
                MethodInfo readMethod = typeof(T).GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(ReadOnlyMemory<byte>)], null)
                    ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Read method.");

                readDelegate = (ReadHandler<T>)Delegate.CreateDelegate(typeof(ReadHandler<T>), readMethod);
                ReadMethodCache[typeof(T)] = readDelegate;
            }

            return ((ReadHandler<T>)readDelegate)(data);
        }
        catch (Exception ex) when (CborError.IsDecodeFailure(ex))
        {
            throw CborError.Wrap(ex, data, typeof(T).FullName!, CborError.Expected(typeof(T)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadNonPrimitive(Type type, ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(type);

        try
        {
            if (Nullable.GetUnderlyingType(type) != null)
            {
                Type underlyingType = Nullable.GetUnderlyingType(type)!;

                if (!ReadMethodCache.TryGetValue(underlyingType, out Delegate? readDelegate))
                {
                    MethodInfo readMethod = underlyingType.GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(ReadOnlyMemory<byte>)], null)
                        ?? throw new NotSupportedException($"Type {underlyingType.FullName} does not have a valid static Read method.");

                    readDelegate = Delegate.CreateDelegate(typeof(ReadHandler<>).MakeGenericType(underlyingType), readMethod);
                    ReadMethodCache[underlyingType] = readDelegate;
                }

                return readDelegate.DynamicInvoke(data);
            }

            if (!ReadMethodCache.TryGetValue(type, out Delegate? readDelegateNonNullable))
            {
                MethodInfo readMethod = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(ReadOnlyMemory<byte>)], null)
                    ?? throw new NotSupportedException($"Type {type.FullName} does not have a valid static Read method.");

                readDelegateNonNullable = Delegate.CreateDelegate(typeof(ReadHandler<>).MakeGenericType(type), readMethod);
                ReadMethodCache[type] = readDelegateNonNullable;
            }

            return readDelegateNonNullable.DynamicInvoke(data);
        }
        catch (Exception exception) when (CborError.IsDecodeFailure(exception))
        {
            throw CborError.Wrap(exception, data, type.FullName!, CborError.Expected(type));
        }
    }

    #region Write

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<T>(IBufferWriter<byte> output, T value)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (IsPrimitiveType(typeof(T)))
        {
            WritePrimitive<T>(output, value);
        }
        else
        {
            WriteNonPrimitive(output, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WritePrimitive<T>(IBufferWriter<byte> output, object? value)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(value);

        CborWriter writer = new(output);
        Type type = typeof(T);

        if (Nullable.GetUnderlyingType(type) != null)
        {
            type = Nullable.GetUnderlyingType(type)!;
        }

        switch (type)
        {
            case Type t when t == typeof(bool): writer.WriteBoolean((bool)value); break;
            case Type t when t == typeof(int): writer.WriteInt32((int)value); break;
            case Type t when t == typeof(uint): writer.WriteUInt32((uint)value); break;
            case Type t when t == typeof(long): writer.WriteInt64((long)value); break;
            case Type t when t == typeof(ulong): writer.WriteUInt64((ulong)value); break;
            case Type t when t == typeof(float): writer.WriteSingle((float)value); break;
            case Type t when t == typeof(double): writer.WriteDouble((double)value); break;
            case Type t when t == typeof(decimal): writer.WriteDecimal((decimal)value); break;
            case Type t when t == typeof(string): writer.WriteString((string)value); break;
            case Type t when t == typeof(byte[]): writer.WriteByteString((byte[])value); break;
            case Type t when t == typeof(ReadOnlyMemory<byte>): writer.WriteByteString(((ReadOnlyMemory<byte>)value).Span); break;
            case Type t when t == typeof(CborEncodedValue):
                CborEncodedValue encoded = (CborEncodedValue)value;
                if (encoded.Raw.Length > 0) { writer.BufferWriter.Write(encoded.Raw.Span); }
                else { writer.WriteSemanticTag(24); writer.WriteByteString(encoded.Value.Span); }
                break;
            case Type t when t == typeof(CborAny): WriteCborAny(output, (CborAny)value); break;
            case Type t when t == typeof(CborLabel):
                CborLabel label = (CborLabel)value;
                switch (label.Value)
                {
                    case int i: writer.WriteInt32(i); break;
                    case long l: writer.WriteInt64(l); break;
                    case string s: writer.WriteString(s); break;
                    default: throw new CborValidationException(-1, "CborLabel", "integer or text string", label.Value?.GetType().FullName ?? "null");
                }
                break;
            default: throw new CborValidationException(-1, type.FullName!, "supported primitive type", type.FullName!);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteNonPrimitive<T>(IBufferWriter<byte> output, T value)
    {
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            if (default(T) is null && typeof(T).IsValueType && value is not null)
            {
                WritePresentNullable(output, Nullable.GetUnderlyingType(typeof(T))!, value);
                return;
            }

            if (!WriteMethodCache.TryGetValue(typeof(T), out Delegate? writeDelegate))
            {
                writeDelegate = CacheWriteHandler(typeof(T), typeof(WriteHandler<T>));
            }

            ((WriteHandler<T>)writeDelegate)(output, value);
        }
        catch (Exception ex) when (CborError.IsDecodeFailure(ex))
        {
            if (ex is CborValidationException) { throw; }
            throw new CborValidationException(-1, typeof(T).FullName!, "serializable value", ex.Message, ex);
        }
    }

    private static void WriteCborAny(IBufferWriter<byte> output, CborAny value)
    {
        ReadOnlySpan<byte> raw = value.Raw.Span;
        if (raw.IsEmpty) { throw CborError.Validation(-1, typeof(CborAny).FullName!, "valid CBOR data item", "empty"); }
        if (raw[0] == 0xFF) { throw CborError.Validation(-1, typeof(CborAny).FullName!, "valid CBOR data item", "unexpected break"); }
        try
        {
            CborReader r = new(raw);
            _ = r.ReadDataItem();
            if (!r.Buffer.IsEmpty) { throw CborError.Validation(-1, typeof(CborAny).FullName!, "single CBOR data item", "trailing bytes"); }
        }
        catch (Exception ex) when (ex is not CborValidationException && CborError.IsDecodeFailure(ex)) { throw CborError.Validation(-1, typeof(CborAny).FullName!, "valid CBOR data item", ex.Message, ex); }
        output.Write(raw);
    }

    private static void WritePresentNullable(IBufferWriter<byte> output, Type underlying, object value)
    {
        // A present nullable boxes as its underlying value. The cache stores the exact writer type.
        if (!WriteMethodCache.TryGetValue(underlying, out Delegate? writeDelegate))
        {
            writeDelegate = CacheWriteHandler(underlying, typeof(WriteHandler<>).MakeGenericType(underlying));
        }
        try { _ = writeDelegate.DynamicInvoke(output, value); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Throw(ex.InnerException); }
    }

    private static Delegate CacheWriteHandler(Type type, Type delegateType)
    {
        MethodInfo write = type.GetMethod("Write", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(IBufferWriter<byte>), type], null)
            ?? throw new NotSupportedException($"Type {type.FullName} does not have a valid static Write method.");
        Delegate handler = write.CreateDelegate(delegateType);
        WriteMethodCache[type] = handler;
        return handler;
    }

    #endregion
}
