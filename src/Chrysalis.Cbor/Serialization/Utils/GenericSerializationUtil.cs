
using System.Collections.Concurrent;
using System.Formats.Cbor;
using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Types;
namespace Chrysalis.Cbor.Serialization.Utils;

/// <summary>
/// Delegate for reading a value of type <typeparamref name="T"/> from CBOR-encoded bytes.
/// </summary>
/// <typeparam name="T">The type to read.</typeparam>
/// <param name="data">The CBOR-encoded data.</param>
/// <returns>The deserialized value.</returns>
public delegate T ReadHandler<T>(ReadOnlyMemory<byte> data);

/// <summary>
/// Delegate for writing a value of type <typeparamref name="T"/> to a CBOR writer.
/// </summary>
/// <typeparam name="T">The type to write.</typeparam>
/// <param name="writer">The CBOR writer.</param>
/// <param name="value">The value to serialize.</param>
public delegate void WriteHandler<T>(CborWriter writer, T value);

/// <summary>
/// Provides utility methods for generic CBOR serialization and deserialization.
/// </summary>
public static class GenericSerializationUtil
{
    private static readonly ConcurrentDictionary<Type, Delegate> ReadMethodCache = [];
    private static readonly ConcurrentDictionary<Type, Delegate> WriteMethodCache = [];

    #region Read

    /// <summary>
    /// Reads a value of type <typeparamref name="T"/> from the CBOR reader.
    /// </summary>
    /// <typeparam name="T">The type to read.</typeparam>
    /// <param name="reader">The CBOR reader.</param>
    /// <returns>The deserialized value, or null.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Read<T>(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return IsPrimitiveType(typeof(T)) ? ReadPrimitive<T>(reader) : ReadNonPrimitive<T>(reader);
    }

    /// <summary>
    /// Reads a value of the specified type from the CBOR reader.
    /// </summary>
    /// <param name="reader">The CBOR reader.</param>
    /// <param name="type">The type to read.</param>
    /// <returns>The deserialized value, or null.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? Read(CborReader reader, Type type)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(type);
        return IsPrimitiveType(type) ? ReadPrimitive(type, reader) : ReadNonPrimitive(type, reader);
    }

    /// <summary>
    /// Determines whether the specified type is a CBOR primitive type.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a primitive CBOR type; otherwise, false.</returns>
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

    /// <summary>
    /// Reads a primitive value of type <typeparamref name="T"/> from the CBOR reader.
    /// </summary>
    /// <typeparam name="T">The primitive type to read.</typeparam>
    /// <param name="reader">The CBOR reader.</param>
    /// <returns>The deserialized primitive value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadPrimitive<T>(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

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
            _ when type == typeof(string) => (T)(object)reader.ReadTextString(),
            _ when type == typeof(byte[]) => (T)(object)ReadByteArray(reader),
            _ when type == typeof(ReadOnlyMemory<byte>) => (T)(object)(ReadOnlyMemory<byte>)ReadByteArray(reader),
            _ when type == typeof(CborEncodedValue) => (T)(object)new CborEncodedValue(reader.ReadEncodedValue()),
            _ when type == typeof(CborLabel) => (T)(object)ReadCborLabel(reader),
            _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
        };
    }

    /// <summary>
    /// Reads a primitive value of the specified type from the CBOR reader.
    /// </summary>
    /// <param name="type">The primitive type to read.</param>
    /// <param name="reader">The CBOR reader.</param>
    /// <returns>The deserialized primitive value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadPrimitive(Type type, CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(reader);

        if (Nullable.GetUnderlyingType(type) != null)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type)!;

            object? value = ReadPrimitive(underlyingType, reader);

            return value;
        }

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
            _ when type == typeof(string) => reader.ReadTextString(),
            _ when type == typeof(byte[]) => ReadByteArray(reader),
            _ when type == typeof(ReadOnlyMemory<byte>) => (ReadOnlyMemory<byte>)ReadByteArray(reader),
            _ when type == typeof(CborEncodedValue) => new CborEncodedValue(reader.ReadEncodedValue()),
            _ when type == typeof(CborLabel) => ReadCborLabel(reader),
            _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CborLabel ReadCborLabel(CborReader reader)
    {
        return reader.PeekState() switch
        {
            CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => new CborLabel(reader.ReadInt64()),
            CborReaderState.TextString => new CborLabel(reader.ReadTextString()),
            CborReaderState.Undefined => throw new NotImplementedException(),
            CborReaderState.ByteString => throw new NotImplementedException(),
            CborReaderState.StartIndefiniteLengthByteString => throw new NotImplementedException(),
            CborReaderState.EndIndefiniteLengthByteString => throw new NotImplementedException(),
            CborReaderState.StartIndefiniteLengthTextString => throw new NotImplementedException(),
            CborReaderState.EndIndefiniteLengthTextString => throw new NotImplementedException(),
            CborReaderState.StartArray => throw new NotImplementedException(),
            CborReaderState.EndArray => throw new NotImplementedException(),
            CborReaderState.StartMap => throw new NotImplementedException(),
            CborReaderState.EndMap => throw new NotImplementedException(),
            CborReaderState.Tag => throw new NotImplementedException(),
            CborReaderState.SimpleValue => throw new NotImplementedException(),
            CborReaderState.HalfPrecisionFloat => throw new NotImplementedException(),
            CborReaderState.SinglePrecisionFloat => throw new NotImplementedException(),
            CborReaderState.DoublePrecisionFloat => throw new NotImplementedException(),
            CborReaderState.Null => throw new NotImplementedException(),
            CborReaderState.Boolean => throw new NotImplementedException(),
            CborReaderState.Finished => throw new NotImplementedException(),
            _ => throw new InvalidOperationException($"Invalid CBOR type for Label: {reader.PeekState()}")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] ReadByteArray(CborReader reader)
    {
        if (reader.PeekState() == CborReaderState.StartIndefiniteLengthByteString)
        {
            reader.ReadStartIndefiniteLengthByteString();
            using MemoryStream stream = new();
            while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)
            {
                byte[] chunk = reader.ReadByteString();
                stream.Write(chunk, 0, chunk.Length);
            }
            reader.ReadEndIndefiniteLengthByteString();
            return stream.ToArray();
        }
        else
        {
            return reader.ReadByteString();
        }
    }

    /// <summary>
    /// Reads a non-primitive value of type <typeparamref name="T"/> from the CBOR reader.
    /// </summary>
    /// <typeparam name="T">The type to read.</typeparam>
    /// <param name="reader">The CBOR reader.</param>
    /// <returns>The deserialized value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadNonPrimitive<T>(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        ReadOnlyMemory<byte> encodedValue = reader.ReadEncodedValue();

        try
        {
            if (!ReadMethodCache.TryGetValue(typeof(T), out Delegate? readDelegate))
            {
                MethodInfo readMethod = typeof(T).GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(ReadOnlyMemory<byte>)], null) ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Read method.");

                readDelegate = (ReadHandler<T>)Delegate.CreateDelegate(typeof(ReadHandler<T>), readMethod);
                ReadMethodCache[typeof(T)] = readDelegate;
            }

            return ((ReadHandler<T>)readDelegate)(encodedValue);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read type {typeof(T).FullName}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads a non-primitive value of the specified type from the CBOR reader.
    /// </summary>
    /// <param name="type">The type to read.</param>
    /// <param name="reader">The CBOR reader.</param>
    /// <returns>The deserialized value, or null.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadNonPrimitive(Type type, CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(reader);

        ReadOnlyMemory<byte> encodedValue = reader.ReadEncodedValue();

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

                object? result = readDelegate.DynamicInvoke(encodedValue);
                return result == null ? null : Convert.ChangeType(result, type, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (!ReadMethodCache.TryGetValue(type, out Delegate? readDelegateNonNullable))
            {
                MethodInfo readMethod = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(ReadOnlyMemory<byte>)], null)
                                     ?? throw new NotSupportedException($"Type {type.FullName} does not have a valid static Read method.");

                readDelegateNonNullable = Delegate.CreateDelegate(typeof(ReadHandler<>).MakeGenericType(type), readMethod);
                ReadMethodCache[type] = readDelegateNonNullable;
            }

            return readDelegateNonNullable.DynamicInvoke(encodedValue);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to read type {type.FullName} via delegate invocation: {ex.Message}", ex);
        }
    }

    #endregion

    #region Write

    /// <summary>
    /// Writes a value of type <typeparamref name="T"/> to the CBOR writer.
    /// </summary>
    /// <typeparam name="T">The type to write.</typeparam>
    /// <param name="writer">The CBOR writer.</param>
    /// <param name="value">The value to serialize.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<T>(CborWriter writer, T value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (IsPrimitiveType(typeof(T)))
        {
            WritePrimitive<T>(writer, value);
        }
        else
        {
            WriteNonPrimitive(writer, value);
        }
    }

    /// <summary>
    /// Writes a primitive value of type <typeparamref name="T"/> to the CBOR writer.
    /// </summary>
    /// <typeparam name="T">The primitive type to write.</typeparam>
    /// <param name="writer">The CBOR writer.</param>
    /// <param name="value">The value to serialize.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WritePrimitive<T>(CborWriter writer, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);

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
                    writer.WriteTextString(stringValue);
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
                    writer.WriteTag(CborTag.EncodedCborDataItem);
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
                            writer.WriteTextString(s);
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

    /// <summary>
    /// Writes a non-primitive value of type <typeparamref name="T"/> to the CBOR writer.
    /// </summary>
    /// <typeparam name="T">The type to write.</typeparam>
    /// <param name="writer">The CBOR writer.</param>
    /// <param name="value">The value to serialize.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteNonPrimitive<T>(CborWriter writer, T value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            if (!WriteMethodCache.TryGetValue(typeof(T), out Delegate? writeDelegate))
            {
                MethodInfo writeMethod = typeof(T).GetMethod("Write", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, [typeof(CborWriter), typeof(T)], null)
                    ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Write method.");

                writeDelegate = (WriteHandler<T>)Delegate.CreateDelegate(typeof(WriteHandler<T>), writeMethod);
                WriteMethodCache[typeof(T)] = writeDelegate;
            }

            ((WriteHandler<T>)writeDelegate)(writer, value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to write type {typeof(T).FullName}: {ex.Message}", ex);
        }
    }

    #endregion
}
