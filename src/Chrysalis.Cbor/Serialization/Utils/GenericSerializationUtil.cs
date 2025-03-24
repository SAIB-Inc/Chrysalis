
using System.Collections.Concurrent;
using System.Formats.Cbor;
using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Serialization.Utils;

public delegate T ReadDelegate<T>(ReadOnlyMemory<byte> data);
public delegate void WriteDelegate<T>(CborWriter writer, T value);

public static class GenericSerializationUtil
{
    private static readonly ConcurrentDictionary<Type, Delegate> ReadMethodCache = [];
    private static readonly ConcurrentDictionary<Type, Delegate> WriteMethodCache = [];

    #region Read

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Read<T>(CborReader reader)
    {
        if (IsPrimitiveType(typeof(T)))
        {
            return ReadPrimitive<T>(reader);
        }
        else
        {
            return ReadNonPrimitive<T>(reader);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? Read(CborReader reader, Type type)
    {
        if (IsPrimitiveType(type))
        {
            return ReadPrimitive(type, reader);
        }
        else
        {
            return ReadNonPrimitive(type, reader);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPrimitiveType(Type type)
    {
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
               type == typeof(CborEncodedValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? ReadPrimitive<T>(CborReader reader)
    {
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
            _ when type == typeof(CborEncodedValue) => (T)(object)new CborEncodedValue(reader.ReadEncodedValue().ToArray()),
            _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadPrimitive(Type type, CborReader reader)
    {
        if (Nullable.GetUnderlyingType(type) != null)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type)!;

            object? value = ReadPrimitive(underlyingType, reader);

            if (value == null)
            {
                return null;
            }
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
            _ when type == typeof(CborEncodedValue) => new CborEncodedValue(reader.ReadEncodedValue().ToArray()),
            _ => throw new NotSupportedException($"Type {type} is not supported as a primitive type.")
        };

        throw new NotSupportedException($"Type {type} is not supported as a primitive type.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] ReadByteArray(CborReader reader)
    {
        if (reader.PeekState() == CborReaderState.StartIndefiniteLengthByteString)
        {
            reader.ReadStartIndefiniteLengthByteString();
            using MemoryStream stream = new MemoryStream();
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadNonPrimitive<T>(CborReader reader)
    {
        ReadOnlyMemory<byte> encodedValue = reader.ReadEncodedValue();

        try
        {
            if (!ReadMethodCache.TryGetValue(typeof(T), out Delegate? readDelegate))
            {
                MethodInfo readMethod = typeof(T).GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof(ReadOnlyMemory<byte>) }, null) ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Read method.");

                readDelegate = (ReadDelegate<T>)Delegate.CreateDelegate(typeof(ReadDelegate<T>), readMethod);
                ReadMethodCache[typeof(T)] = readDelegate;
            }

            return ((ReadDelegate<T>)readDelegate)(encodedValue);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read type {typeof(T).FullName}: {ex.Message}", ex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ReadNonPrimitive(Type type, CborReader reader)
    {
        ReadOnlyMemory<byte> encodedValue = reader.ReadEncodedValue();

        try
        {
            if (Nullable.GetUnderlyingType(type) != null)
            {
                Type underlyingType = Nullable.GetUnderlyingType(type)!;

                if (!ReadMethodCache.TryGetValue(underlyingType, out Delegate? readDelegate))
                {
                    MethodInfo readMethod = underlyingType.GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof(ReadOnlyMemory<byte>) }, null)
                                         ?? throw new NotSupportedException($"Type {underlyingType.FullName} does not have a valid static Read method.");

                    readDelegate = Delegate.CreateDelegate(typeof(ReadDelegate<>).MakeGenericType(underlyingType), readMethod);
                    ReadMethodCache[underlyingType] = readDelegate;
                }

                object? result = readDelegate.DynamicInvoke(encodedValue);
                return result == null ? null : Convert.ChangeType(result, type);
            }

            if (!ReadMethodCache.TryGetValue(type, out Delegate? readDelegateNonNullable))
            {
                MethodInfo readMethod = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof(ReadOnlyMemory<byte>) }, null)
                                     ?? throw new NotSupportedException($"Type {type.FullName} does not have a valid static Read method.");

                readDelegateNonNullable = Delegate.CreateDelegate(typeof(ReadDelegate<>).MakeGenericType(type), readMethod);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<T>(CborWriter writer, T value)
    {
        if (IsPrimitiveType(typeof(T)))
        {
            WritePrimitive<T>(writer, value);
        }
        else
        {
            WriteNonPrimitive(writer, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WritePrimitive<T>(CborWriter writer, object? value)
    {
        Type type = typeof(T);

        if (Nullable.GetUnderlyingType(type) != null)
        {
            type = Nullable.GetUnderlyingType(type)!;
        }

        switch (type)
        {
            case Type t when t == typeof(bool):
                if (value is bool boolValue)
                    writer.WriteBoolean(boolValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(int):
                if (value is int intValue)
                    writer.WriteInt32(intValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(uint):
                if (value is uint uintValue)
                    writer.WriteUInt32(uintValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(long):
                if (value is long longValue)
                    writer.WriteInt64(longValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(ulong):
                if (value is ulong ulongValue)
                    writer.WriteUInt64(ulongValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(float):
                if (value is float floatValue)
                    writer.WriteSingle(floatValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(double):
                if (value is double doubleValue)
                    writer.WriteDouble(doubleValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(decimal):
                if (value is decimal decimalValue)
                    writer.WriteDecimal(decimalValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(string):
                if (value is string stringValue)
                    writer.WriteTextString(stringValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(byte[]):
                if (value is byte[] bytesValue)
                    writer.WriteByteString(bytesValue);
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            case Type t when t == typeof(CborEncodedValue):
                if (value is CborEncodedValue encodedValue)
                {
                    writer.WriteTag(CborTag.EncodedCborDataItem);
                    writer.WriteByteString(encodedValue.Value);
                }
                else
                    throw new InvalidCastException($"Value is not of type {type}");
                break;

            default:
                throw new NotSupportedException($"Type {type} is not supported as a primitive type.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteNonPrimitive<T>(CborWriter writer, T value)
    {
        try
        {
            if (!WriteMethodCache.TryGetValue(typeof(T), out Delegate? writeDelegate))
            {
                MethodInfo writeMethod = typeof(T).GetMethod("Write", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof(CborWriter), typeof(T) }, null)
                    ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Write method.");

                writeDelegate = (WriteDelegate<T>)Delegate.CreateDelegate(typeof(WriteDelegate<T>), writeMethod);
                WriteMethodCache[typeof(T)] = writeDelegate;
            }

            ((WriteDelegate<T>)writeDelegate)(writer, value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to write type {typeof(T).FullName}: {ex.Message}", ex);
        }
    }

    #endregion
}
