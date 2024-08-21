using System;
using System.Collections;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Chrysalis.Cbor;
public static class CborSerializer
{
    public static byte[] Serialize(object obj)
    {
        CborWriter writer = new();
        {
            return SerializePrimitive(writer, obj, obj.GetType());
        }
    }

    public static object Deserialize(byte[] data, Type targetType)
    {
        CborReader reader = new(data);
        {
            return DeserializePrimitive(reader, targetType, null)!;
        }
    }
    public static void SerializePrimitive(CborWriter writer, object obj, Type objType, CborType? overrideType = null)
    {
        CborType cborType = overrideType ?? DetermineCborType(objType);

        switch (cborType)
        {
            case CborType.ByteString:
                switch (obj)
                {
                    case byte[] byteArray:
                        writer.WriteByteString(byteArray);
                        break;
                    case string hexString:
                        writer.WriteByteString(Convert.FromHexString(hexString));
                        break;
                    default:
                        throw new InvalidOperationException("Expected a byte array or hex string for ByteString type.");
                }
                break;

            case CborType.Int:
                switch (obj)
                {
                    case int intValue:
                        writer.WriteInt32(intValue);
                        break;
                    case long longValue:
                        writer.WriteInt64(longValue);
                        break;
                    default:
                        throw new InvalidOperationException("Expected an integer for Int type.");
                }
                break;

            case CborType.Ulong:
                if (obj is ulong ulongValue)
                {
                    writer.WriteUInt64(ulongValue);
                }
                else
                {
                    throw new InvalidOperationException("Expected an unsigned long for Ulong type.");
                }
                break;

            case CborType.List:
                if (obj is IEnumerable<object> enumerable)
                {
                    Type elementType = obj.GetType().GetGenericArguments().FirstOrDefault() ?? typeof(object);
                    SerializeList(writer, enumerable, elementType);
                }
                else
                {
                    throw new InvalidOperationException("Expected an enumerable for List type.");
                }
                break;

            case CborType.Map:
                SerializeMap(writer, obj, objType);
                break;

            case CborType.Union:
                SerializeUnion(writer, obj);
                break;

            default:
                throw new NotSupportedException($"CBOR type {cborType} is not supported in this context.");
        }
    }

    public static void SerializeList(CborWriter writer, object obj, Type elementType, bool indefinite = false)
    {
        if (!typeof(IEnumerable).IsAssignableFrom(elementType))
            throw new ArgumentException("Type must be an enumerable to be deserialized as a CBOR array");

        if (obj is IEnumerable<object> enumerable)
        {
            if (indefinite)
            {
                writer.WriteStartArray(0);
                foreach (var element in enumerable)
                {
                    SerializePrimitive(writer, element, elementType);
                }
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteStartArray(enumerable.Count());
                foreach (var element in enumerable)
                {
                    SerializePrimitive(writer, element, elementType);
                }
                writer.WriteEndArray();
            }
        }
    }

    public static void SerializeMap(CborWriter writer, object obj, Type objType)
    {
        if (objType == typeof(Dictionary<ICbor, ICbor>))
        {
            if (obj is Dictionary<ICbor, ICbor> map)
            {
                writer.WriteStartMap(map.Count);
                foreach (var kvp in map)
                {
                    SerializePrimitive(writer, kvp.Key, kvp.Key.GetType());
                    SerializePrimitive(writer, kvp.Value, kvp.Value.GetType());
                }
                writer.WriteEndMap();
            }
            else
            {
                throw new InvalidOperationException("Expected a Dictionary<ICbor, ICbor> for Map type.");
            }
        }
        else
        {
            Type genericMapType = objType.GetGenericTypeDefinition();
            if (genericMapType == typeof(Dictionary<,>))
            {
                Type keyType = objType.GetGenericArguments()[0];
                Type valueType = objType.GetGenericArguments()[1];
                if (obj is IDictionary dictionary)
                {
                    writer.WriteStartMap(dictionary.Count);
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        SerializePrimitive(writer, entry.Key, keyType);
                        SerializePrimitive(writer, entry.Value ?? string.Empty, valueType);
                    }
                    writer.WriteEndMap();
                }
                else
                {
                    throw new InvalidOperationException("Expected a dictionary for Map type.");
                }
            }
            else
            {
                throw new InvalidOperationException("Unsupported map type.");
            }
        }
    }

    public static void SerializeUnion(CborWriter writer, object obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        Type objType = obj.GetType();
        var unionAttribute = objType.GetCustomAttribute<CborUnionTypesAttribute>();

        if (unionAttribute != null)
        {
            SerializeUnionType(writer, obj, objType, unionAttribute.UnionTypes);
            return;
        }

        SerializePrimitive(writer, obj, objType);
    }

    public static void SerializeUnionType(CborWriter writer, object obj, Type objType, Type[] unionTypes)
    {
        foreach (var unionType in unionTypes)
        {
            if (unionType.IsInstanceOfType(obj))
            {
                writer.WriteStartMap(2);
                writer.WriteInt32(Array.IndexOf(unionTypes, unionType));
                SerializePrimitive(writer, obj, objType);
                writer.WriteEndMap();
                return;
            }
        }
    }

    public static object? DeserializePrimitive(CborReader reader, Type targetType, CborType? cborType)
    {
        cborType ??= DetermineCborType(targetType);

        return cborType switch
        {
            CborType.ByteString => DeserializeByteString(reader, targetType),
            CborType.Int => DeserializeInt(reader, targetType),
            CborType.Ulong => DeserializeUlong(reader, targetType),
            CborType.List => DeserializeList(reader, targetType),
            CborType.Map => DeserializeMap(reader, targetType),
            CborType.Union => DeserializeUnion(reader, targetType),
            _ => throw new NotSupportedException($"CBOR type {cborType} is not supported for deserialization.")
        };
    }

    public static object? DeserializeByteString(CborReader reader, Type targetType)
    {
        if (targetType == typeof(byte[]))
        {
            return reader.ReadByteString();
        }
        if (targetType == typeof(string))
        {
            var byteArray = reader.ReadByteString();
            return BitConverter.ToString(byteArray).Replace("-", "").ToLower();
        }
        throw new InvalidOperationException($"Unsupported target type for ByteString: {targetType.Name}");
    }

    public static object? DeserializeInt(CborReader reader, Type targetType)
    {
        if (targetType == typeof(int))
        {
            return reader.ReadInt32();
        }
        if (targetType == typeof(long))
        {
            return reader.ReadInt64();
        }
        throw new InvalidOperationException($"Unsupported target type for Int: {targetType.Name}");
    }

    public static object? DeserializeUlong(CborReader reader, Type targetType)
    {
        if (targetType == typeof(ulong))
        {
            return reader.ReadUInt64();
        }
        throw new InvalidOperationException($"Unsupported target type for Ulong: {targetType.Name}");
    }

    public static object? DeserializeList(CborReader reader, Type targetType)
    {
        if (!typeof(IEnumerable).IsAssignableFrom(targetType) || !targetType.IsGenericType)
            throw new InvalidOperationException("Target type must be an enumerable for List deserialization.");

        Type elementType = targetType.GetGenericArguments().FirstOrDefault() ?? typeof(object);
        IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;

        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            var element = DeserializePrimitive(reader, elementType, null);
            list.Add(element);
        }
        reader.ReadEndArray();

        return list;
    }
    public static object? DeserializeMap(CborReader reader, Type targetType)
    {
        if (typeof(IDictionary).IsAssignableFrom(targetType) && targetType.IsGenericType)
        {
            var keyType = targetType.GetGenericArguments()[0];
            var valueType = targetType.GetGenericArguments()[1];
            var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType))!;

            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndArray)
            {
                var key = DeserializePrimitive(reader, keyType, null);
                var value = DeserializePrimitive(reader, valueType, null);
                dictionary.Add(key!, value);
            }
            reader.ReadEndMap();

            return dictionary;
        }

        throw new InvalidOperationException($"Unsupported target type for Map: {targetType.Name}");
    }

    public static object? DeserializeUnion(CborReader reader, Type targetType)
    {
        var unionAttribute = targetType.GetCustomAttribute<CborUnionTypesAttribute>() ?? throw new InvalidOperationException("Union type must have a CborUnionTypesAttribute.");
        
        reader.ReadStartMap();
        var unionTypeIndex = reader.ReadInt32();
        var unionType = unionAttribute.UnionTypes[unionTypeIndex];

        var value = DeserializePrimitive(reader, unionType, null);
        reader.ReadEndMap();

        return value;
    }

    private static CborType DetermineCborType(Type objType)
    {
        CborUnionTypesAttribute? unionAttribute = objType.GetCustomAttribute<CborUnionTypesAttribute>();
        if (unionAttribute != null)
        {
            return CborType.Union;
        }

        if (objType == typeof(byte[]))
            return CborType.ByteString;
            
        if (objType == typeof(int) || objType == typeof(long))
            return CborType.Int;

        if (objType == typeof(uint) || objType == typeof(ulong))
            return CborType.Ulong;

        if (typeof(IEnumerable).IsAssignableFrom(objType) && objType.IsGenericType)
        {
            Type genericType = objType.GetGenericTypeDefinition();
            if (genericType == typeof(List<>) || genericType == typeof(IEnumerable<>))
                return CborType.List;
        }

        if (typeof(IDictionary).IsAssignableFrom(objType) && objType.IsGenericType)
        {
            Type genericType = objType.GetGenericTypeDefinition();
            if (genericType == typeof(Dictionary<,>))
                return CborType.Map;
        }

        throw new NotSupportedException($"The object type {objType.Name} is not supported for serialization.");
    }
}