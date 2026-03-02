using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Types;
using Dahomey.Cbor.Serialization;
namespace Chrysalis.Cbor.Serialization.Utils;

/// <summary>
/// Delegate for reading a value of type <typeparamref name="T"/> from CBOR-encoded bytes.
/// </summary>
public delegate T ReadHandler<T>(ReadOnlyMemory<byte> data);

/// <summary>
/// Delegate for reading a value of type <typeparamref name="T"/> from CBOR-encoded bytes with consumed byte tracking.
/// </summary>
public delegate T ReadWithConsumedHandler<T>(ReadOnlyMemory<byte> data, out int bytesConsumed);

/// <summary>
/// Delegate for writing a value of type <typeparamref name="T"/> to an output buffer.
/// </summary>
public delegate void WriteHandler<T>(IBufferWriter<byte> output, T value);

/// <summary>
/// Provides utility methods for generic CBOR serialization and deserialization.
/// </summary>
public static class GenericSerializationUtil
{
    private static readonly ConcurrentDictionary<Type, Delegate> ReadMethodCache = [];
    private static readonly ConcurrentDictionary<Type, Delegate> ReadWithConsumedMethodCache = [];
    private static readonly ConcurrentDictionary<Type, Delegate> WriteMethodCache = [];

    #region Read

    /// <summary>Reads a value of type T from CBOR-encoded bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Read<T>(ReadOnlyMemory<byte> data)
    {
        return IsPrimitiveType(typeof(T)) ? ReadPrimitive<T>(data) : ReadNonPrimitive<T>(data);
    }

    /// <summary>Reads a value of type T from CBOR-encoded bytes and reports how many bytes were consumed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadWithConsumed<T>(ReadOnlyMemory<byte> data, out int bytesConsumed) where T : CborBase
    {
        return ReadNonPrimitiveWithConsumed<T>(data, out bytesConsumed);
    }

    /// <summary>Reads a value of type T (primitive or non-primitive) from CBOR-encoded bytes and reports consumed bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadAnyWithConsumed<T>(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        if (IsPrimitiveType(typeof(T)))
        {
            return ReadPrimitiveWithConsumed<T>(data, out bytesConsumed);
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

    /// <summary>Reads a primitive value of type T from CBOR-encoded bytes and reports consumed bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadPrimitiveWithConsumed<T>(ReadOnlyMemory<byte> data, out int bytesConsumed)
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
            _ when type == typeof(ReadOnlyMemory<byte>) => (T)(object)(ReadOnlyMemory<byte>)ReadByteArray(ref reader),
            _ when type == typeof(CborEncodedValue) => ReadCborEncodedValueAsT<T>(ref reader, data),
            _ when type == typeof(CborLabel) => (T)(object)ReadCborLabel(ref reader),
            _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
        };

        bytesConsumed = data.Length - reader.Buffer.Length;
        return result;
    }

    /// <summary>Reads a non-primitive value of type T from CBOR-encoded bytes, reporting consumed bytes via the generated Read(data, out bytesConsumed) overload.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadNonPrimitiveWithConsumed<T>(ReadOnlyMemory<byte> data, out int bytesConsumed) where T : CborBase
    {
        if (!ReadWithConsumedMethodCache.TryGetValue(typeof(T), out Delegate? readDelegate))
        {
            MethodInfo readMethod = typeof(T).GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(ReadOnlyMemory<byte>), typeof(int).MakeByRefType()], null)
                ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Read(ReadOnlyMemory<byte>, out int) method.");

            readDelegate = (ReadWithConsumedHandler<T>)Delegate.CreateDelegate(typeof(ReadWithConsumedHandler<T>), readMethod);
            ReadWithConsumedMethodCache[typeof(T)] = readDelegate;
        }

        return ((ReadWithConsumedHandler<T>)readDelegate)(data, out bytesConsumed);
    }

    /// <summary>Reads a value of the specified type from CBOR-encoded bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? Read(ReadOnlyMemory<byte> data, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return IsPrimitiveType(type) ? ReadPrimitive(type, data) : ReadNonPrimitive(type, data);
    }

    /// <summary>Reads a value of the specified type from CBOR-encoded bytes and reports consumed bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadAnyWithConsumed(ReadOnlyMemory<byte> data, Type type, out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(type);
        return IsPrimitiveType(type)
            ? ReadPrimitiveWithConsumed(type, data, out bytesConsumed)
            : ReadNonPrimitiveWithConsumed(type, data, out bytesConsumed);
    }

    /// <summary>Determines whether the specified type is a CBOR primitive type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPrimitiveType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = Nullable.GetUnderlyingType(type)!;
        }

        return type == typeof(bool) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal) ||
               type == typeof(string) ||
               type == typeof(byte[]) ||
               type == typeof(ReadOnlyMemory<byte>) ||
               type == typeof(CborEncodedValue) ||
               type == typeof(CborLabel);
    }

    /// <summary>Reads a primitive value of type T from CBOR-encoded bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadPrimitive<T>(ReadOnlyMemory<byte> data)
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
            _ when type == typeof(ReadOnlyMemory<byte>) => (T)(object)(ReadOnlyMemory<byte>)ReadByteArray(ref reader),
            _ when type == typeof(CborEncodedValue) => (T)(object)new CborEncodedValue(data),
            _ when type == typeof(CborLabel) => (T)(object)ReadCborLabel(ref reader),
            _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
        };
    }

    /// <summary>Reads a primitive value of the specified type from CBOR-encoded bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadPrimitive(Type type, ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (Nullable.GetUnderlyingType(type) != null)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type)!;
            object? value = ReadPrimitive(underlyingType, data);
            return value;
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
            _ when type == typeof(ReadOnlyMemory<byte>) => (ReadOnlyMemory<byte>)ReadByteArray(ref reader),
            _ when type == typeof(CborEncodedValue) => new CborEncodedValue(data),
            _ when type == typeof(CborLabel) => ReadCborLabel(ref reader),
            _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
        };
    }

    /// <summary>Reads a primitive value of the specified type from CBOR-encoded bytes and reports consumed bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? ReadPrimitiveWithConsumed(Type type, ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);

        if (Nullable.GetUnderlyingType(type) != null)
        {
            type = Nullable.GetUnderlyingType(type)!;
        }

        // CborEncodedValue needs special handling (requires ReadDataItem for boundary)
        if (type == typeof(CborEncodedValue))
        {
            int pos = data.Length - reader.Buffer.Length;
            _ = reader.ReadDataItem();
            bytesConsumed = data.Length - reader.Buffer.Length;
            return new CborEncodedValue(data[pos..bytesConsumed]);
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
            _ when type == typeof(ReadOnlyMemory<byte>) => (ReadOnlyMemory<byte>)ReadByteArray(ref reader),
            _ when type == typeof(CborLabel) => ReadCborLabel(ref reader),
            _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
        };
        bytesConsumed = data.Length - reader.Buffer.Length;
        return result;
    }

    /// <summary>Reads a non-primitive value of the specified type from CBOR-encoded bytes and reports consumed bytes.</summary>
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
        object? result = readMethod.Invoke(null, args);
        bytesConsumed = (int)args[1]!;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CborLabel ReadCborLabel(ref CborReader reader)
    {
        CborDataItemType itemType = reader.GetCurrentDataItemType();
        return itemType switch
        {
            CborDataItemType.Unsigned or CborDataItemType.Signed => new CborLabel(reader.ReadInt64()),
            CborDataItemType.String => new CborLabel(reader.ReadString()!),
            CborDataItemType.Boolean or CborDataItemType.Null or CborDataItemType.Single or CborDataItemType.Double
                or CborDataItemType.ByteString or CborDataItemType.Array or CborDataItemType.Map
                or CborDataItemType.Break or CborDataItemType.Decimal
                => throw new InvalidOperationException($"Invalid CBOR type for Label: {itemType}"),
            _ => throw new InvalidOperationException($"Invalid CBOR type for Label: {itemType}")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ReadCborEncodedValueAsT<T>(ref CborReader reader, ReadOnlyMemory<byte> data)
    {
        int pos = data.Length - reader.Buffer.Length;
        _ = reader.ReadDataItem();
        int consumed = data.Length - reader.Buffer.Length - pos;
        return (T)(object)new CborEncodedValue(data.Slice(pos, consumed));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] ReadByteArray(ref CborReader reader)
    {
        // Check for indefinite byte string (0x5F)
        if (reader.Buffer.Length > 0 && reader.Buffer[0] == 0x5F)
        {
            _ = reader.ReadDataItem(); // skip 0x5F
            using MemoryStream stream = new();
            while (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF)
            {
                ReadOnlySpan<byte> chunk = reader.ReadByteString();
                stream.Write(chunk);
            }
            _ = reader.ReadDataItem(); // skip 0xFF break
            return stream.ToArray();
        }
        else
        {
            return reader.ReadByteString().ToArray();
        }
    }

    /// <summary>Reads a non-primitive value of type T from CBOR-encoded bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadNonPrimitive<T>(ReadOnlyMemory<byte> data)
    {
        try
        {
            if (!ReadMethodCache.TryGetValue(typeof(T), out Delegate? readDelegate))
            {
                MethodInfo readMethod = typeof(T).GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(ReadOnlyMemory<byte>)], null) ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Read method.");

                readDelegate = (ReadHandler<T>)Delegate.CreateDelegate(typeof(ReadHandler<T>), readMethod);
                ReadMethodCache[typeof(T)] = readDelegate;
            }

            return ((ReadHandler<T>)readDelegate)(data);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read type {typeof(T).FullName}: {ex.Message}", ex);
        }
    }

    /// <summary>Reads a non-primitive value of the specified type from CBOR-encoded bytes.</summary>
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

                object? result = readDelegate.DynamicInvoke(data);
                return result == null ? null : Convert.ChangeType(result, type, System.Globalization.CultureInfo.InvariantCulture);
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
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to read type {type.FullName} via delegate invocation: {ex.Message}", ex);
        }
    }

    #endregion

    #region Write

    /// <summary>Writes a value of type T to the output buffer.</summary>
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

    /// <summary>Writes a primitive value of type T to the output buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WritePrimitive<T>(IBufferWriter<byte> output, object? value)
    {
        ArgumentNullException.ThrowIfNull(output);

        CborWriter writer = new(output);

        Type type = typeof(T);

        if (Nullable.GetUnderlyingType(type) != null)
        {
            type = Nullable.GetUnderlyingType(type)!;
        }

        switch (type)
        {
            case Type t when t == typeof(bool):
                if (value is bool boolValue)
                {
                    writer.WriteBoolean(boolValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(int):
                if (value is int intValue)
                {
                    writer.WriteInt32(intValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(uint):
                if (value is uint uintValue)
                {
                    writer.WriteUInt32(uintValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(long):
                if (value is long longValue)
                {
                    writer.WriteInt64(longValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(ulong):
                if (value is ulong ulongValue)
                {
                    writer.WriteUInt64(ulongValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(float):
                if (value is float floatValue)
                {
                    writer.WriteSingle(floatValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(double):
                if (value is double doubleValue)
                {
                    writer.WriteDouble(doubleValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(decimal):
                if (value is decimal decimalValue)
                {
                    writer.WriteDecimal(decimalValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(string):
                if (value is string stringValue)
                {
                    writer.WriteString(stringValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(byte[]):
                if (value is byte[] bytesValue)
                {
                    writer.WriteByteString(bytesValue);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(ReadOnlyMemory<byte>):
                if (value is ReadOnlyMemory<byte> memoryValue)
                {
                    writer.WriteByteString(memoryValue.Span);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(CborEncodedValue):
                if (value is CborEncodedValue encodedValue)
                {
                    writer.WriteSemanticTag(24);
                    writer.WriteByteString(encodedValue.Value.Span);
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            case Type t when t == typeof(CborLabel):
                if (value is CborLabel label)
                {
                    switch (label.Value)
                    {
                        case int i:
                            writer.WriteInt32(i);
                            break;
                        case long l:
                            writer.WriteInt64(l);
                            break;
                        case string s:
                            writer.WriteString(s);
                            break;
                        default:
                            throw new InvalidOperationException($"CborLabel value must be int, long, or string. Got: {label.Value?.GetType()}");
                    }
                }
                else
                {
                    throw new InvalidCastException($"Value is not of type {type}");
                }

                break;

            default:
                throw new NotSupportedException($"Type {type} is not supported as a primitive type.");
        }
    }

    /// <summary>Writes a non-primitive value of type T to the output buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteNonPrimitive<T>(IBufferWriter<byte> output, T value)
    {
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            if (!WriteMethodCache.TryGetValue(typeof(T), out Delegate? writeDelegate))
            {
                MethodInfo writeMethod = typeof(T).GetMethod("Write", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(IBufferWriter<byte>), typeof(T)], null)
                    ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Write method.");

                writeDelegate = (WriteHandler<T>)Delegate.CreateDelegate(typeof(WriteHandler<T>), writeMethod);
                WriteMethodCache[typeof(T)] = writeDelegate;
            }

            ((WriteHandler<T>)writeDelegate)(output, value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to write type {typeof(T).FullName}: {ex.Message}", ex);
        }
    }

    #endregion
}
