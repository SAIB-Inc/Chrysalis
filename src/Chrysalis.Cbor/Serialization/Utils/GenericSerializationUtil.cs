
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Serialization.Utils;

public delegate T ReadDelegate<T>(ReadOnlyMemory<byte> data);

public static class GenericSerializationUtil
{
    private static readonly Dictionary<Type, Delegate> ReadMethodCache = [];

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

    public static object? Read(CborReader reader, object? value)
    {
        Type? type = value?.GetType();

        if (type == null)
        {
            return null;
        }

        if (IsPrimitiveType(type))
        {
            return ReadPrimitive(type, reader);
        }
        else
        {
            return ReadNonPrimitive(type, reader);
        }
    }

    public static bool IsPrimitiveType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = Nullable.GetUnderlyingType(type)!;
        }

        // Now check if the type is a primitive type or one of the supported types
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

    public static T? ReadPrimitive<T>(CborReader reader)
    {
        Type type = typeof(T);

        // Handle nullable types by getting the underlying type
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            // Get the underlying type of the nullable
            type = Nullable.GetUnderlyingType(type)!;
        }

        // Handle primitive types (including nullable ones)
        if (type == typeof(bool))
            return (T)(object)reader.ReadBoolean();
        if (type == typeof(int))
            return (T)(object)reader.ReadInt32();
        if (type == typeof(uint))
            return (T)(object)reader.ReadUInt32();
        if (type == typeof(long))
            return (T)(object)reader.ReadInt64();
        if (type == typeof(ulong))
            return (T)(object)reader.ReadUInt64();
        if (type == typeof(float))
            return (T)(object)reader.ReadSingle();
        if (type == typeof(double))
            return (T)(object)reader.ReadDouble();
        if (type == typeof(decimal))
            return (T)(object)reader.ReadDecimal();
        if (type == typeof(string))
            return (T)(object)reader.ReadTextString();
        if (type == typeof(byte[]))
            return (T)(object)ReadByteArray(reader);
        if (type == typeof(CborEncodedValue))
            return (T)(object)reader.ReadEncodedValue();

        // Special handling for nullable types
        if (Nullable.GetUnderlyingType(type) != null)
        {
            // Handle the nullable case (reading the underlying type)
            object? value = ReadPrimitive(Nullable.GetUnderlyingType(type)!, reader);  // Recursively call to read underlying type
            return (T?)value;  // Convert back to Nullable<T>
        }

        throw new NotSupportedException($"Type {type} is not supported as a primitive type.");
    }

    public static object? ReadPrimitive(Type type, CborReader reader)
    {
        // Handle nullable types by checking the underlying type
        if (Nullable.GetUnderlyingType(type) != null)
        {
            // Get the underlying type of the nullable
            Type underlyingType = Nullable.GetUnderlyingType(type)!;

            // Read the value for the underlying type (it could be null)
            object? value = ReadPrimitive(underlyingType, reader);

            // If value is null, return it as Nullable<T> (null wrapped in nullable type)
            if (value == null)
            {
                return null; // This will return a nullable type (e.g., ulong?)
            }

            // Convert the value to the nullable type (T?)
            return value;
        }

        // Handle non-nullable types
        if (type == typeof(bool))
            return reader.ReadBoolean();
        if (type == typeof(int))
            return reader.ReadInt32();
        if (type == typeof(uint))
            return reader.ReadUInt32();
        if (type == typeof(long))
            return reader.ReadInt64();
        if (type == typeof(ulong))
            return reader.ReadUInt64();
        if (type == typeof(float))
            return reader.ReadSingle();
        if (type == typeof(double))
            return reader.ReadDouble();
        if (type == typeof(decimal))
            return reader.ReadDecimal();
        if (type == typeof(string))
            return reader.ReadTextString();
        if (type == typeof(byte[]))
            return ReadByteArray(reader);
        if (type == typeof(CborEncodedValue))
            return reader.ReadEncodedValue();

        throw new NotSupportedException($"Type {type} is not supported as a primitive type.");
    }


    private static byte[] ReadByteArray(CborReader reader)
    {
        if (reader.PeekState() == CborReaderState.StartIndefiniteLengthByteString)
        {
            reader.ReadStartIndefiniteLengthByteString();
            using var stream = new MemoryStream();
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

    public static T ReadNonPrimitive<T>(CborReader reader)
    {
        // Read the encoded value
        ReadOnlyMemory<byte> encodedValue = reader.ReadEncodedValue();

        try
        {
            // Check if we have already cached the delegate for this type
            if (!ReadMethodCache.TryGetValue(typeof(T), out var readDelegate))
            {
                // If not cached, use reflection to find the Read method and create a delegate
                var readMethod = typeof(T).GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof(ReadOnlyMemory<byte>) }, null) ?? throw new NotSupportedException($"Type {typeof(T).FullName} does not have a valid static Read method.");

                // Create the delegate and cache it
                readDelegate = (ReadDelegate<T>)Delegate.CreateDelegate(typeof(ReadDelegate<T>), readMethod);
                ReadMethodCache[typeof(T)] = readDelegate;
            }

            // Invoke the cached delegate
            return ((ReadDelegate<T>)readDelegate)(encodedValue);
        }
        catch (Exception ex)
        {
            // Log and throw any errors that occur
            throw new InvalidOperationException($"Failed to read type {typeof(T).FullName}: {ex.Message}", ex);
        }
    }

    public static object? ReadNonPrimitive(Type type, CborReader reader)
    {
        // Read the encoded value
        ReadOnlyMemory<byte> encodedValue = reader.ReadEncodedValue();

        try
        {
            // Handle nullable types: check if type is Nullable<T>
            if (Nullable.GetUnderlyingType(type) != null)
            {
                // Get the underlying type (e.g., ulong for ulong?)
                Type underlyingType = Nullable.GetUnderlyingType(type)!;

                // Check if we have already cached the delegate for the underlying type
                if (!ReadMethodCache.TryGetValue(underlyingType, out var readDelegate))
                {
                    // If not cached, use reflection to find the Read method and create a delegate
                    var readMethod = underlyingType.GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof(ReadOnlyMemory<byte>) }, null)
                                         ?? throw new NotSupportedException($"Type {underlyingType.FullName} does not have a valid static Read method.");

                    // Create the delegate for the specific type
                    readDelegate = (Delegate)Delegate.CreateDelegate(typeof(ReadDelegate<>).MakeGenericType(underlyingType), readMethod);
                    ReadMethodCache[underlyingType] = readDelegate;
                }

                // Invoke the cached delegate for the underlying type and return the result wrapped as Nullable<T>
                object? result = readDelegate.DynamicInvoke(encodedValue);
                return result == null ? null : Convert.ChangeType(result, type);
            }

            // Handle non-nullable types: Use delegates for non-nullable types as well
            if (!ReadMethodCache.TryGetValue(type, out var readDelegateNonNullable))
            {
                // If not cached, use reflection to find the Read method and create a delegate
                var readMethod = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, new[] { typeof(ReadOnlyMemory<byte>) }, null)
                                     ?? throw new NotSupportedException($"Type {type.FullName} does not have a valid static Read method.");

                // Create the delegate for the specific type
                readDelegateNonNullable = (Delegate)Delegate.CreateDelegate(typeof(ReadDelegate<>).MakeGenericType(type), readMethod);
                ReadMethodCache[type] = readDelegateNonNullable;
            }

            // Invoke the cached delegate for the non-nullable type
            return readDelegateNonNullable.DynamicInvoke(encodedValue);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Provide better diagnostics
            throw new InvalidOperationException($"Failed to read type {type.FullName} via delegate invocation: {ex.Message}", ex);
        }
    }


    public static List<T> ReadList<T>(CborReader reader)
    {
        List<T> result = [];
        reader.ReadStartArray();

        while (reader.PeekState() != CborReaderState.EndArray)
        {
            T item;
            if (IsPrimitiveType(typeof(T)))
            {
                item = ReadPrimitive<T>(reader);
            }
            else
            {
                item = ReadNonPrimitive<T>(reader);
            }
            result.Add(item);
        }

        reader.ReadEndArray();
        return result;
    }

    public static Dictionary<TKey, TValue> ReadDictionary<TKey, TValue>(CborReader reader)
        where TKey : notnull
    {
        Dictionary<TKey, TValue> result = [];
        reader.ReadStartMap();

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            TKey key;
            if (IsPrimitiveType(typeof(TKey)))
            {
                key = ReadPrimitive<TKey>(reader);
            }
            else
            {
                key = ReadNonPrimitive<TKey>(reader);
            }

            TValue value;
            if (IsPrimitiveType(typeof(TValue)))
            {
                value = ReadPrimitive<TValue>(reader);
            }
            else
            {
                value = ReadNonPrimitive<TValue>(reader);
            }

            result.Add(key, value);
        }

        reader.ReadEndMap();
        return result;
    }

    public static dynamic? ConvertValue(object value, Type targetType)
    {
        if (value == null) return null;
        if (value.GetType() == targetType) return value;
        if (targetType.IsPrimitive || targetType == typeof(string) || targetType == typeof(decimal))
            return Convert.ChangeType(value, targetType);

        return value;
    }
}
