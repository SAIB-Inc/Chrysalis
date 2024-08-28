using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using System.Text;
using Chrysalis.Cardano.Models;

namespace Chrysalis.Cbor;
public static class CborSerializer
{
    public static byte[] Serialize(ICbor cbor)
    {
        CborWriter writer = new(CborConformanceMode.Strict);
        SerializeCbor(writer, cbor, cbor.GetType());
        return writer.Encode();
    }

    public static T? Deserialize<T>(byte[] cborData, Type? type = null)
    {
        CborReader reader = new(cborData, CborConformanceMode.Strict);
        Type targetType = type ?? typeof(T);
        return (T?)DeserializeCbor(reader, targetType);
    }

    private static void SerializeCbor(CborWriter writer, ICbor cbor, Type objType, CborType? overrideType = null)
    {
        CborType? cborType = null;
        if (objType != typeof(ICbor))
            cborType = overrideType ?? DetermineCborType(objType);
        else
        {
            switch (cbor)
            {
                case CborBytes cborBytes:
                    SerializeCborBytes(writer, cbor, objType);
                    break;
                case CborInt cborInt:
                    SerializeCborInt(writer, cbor, objType);
                    break;
                case CborUlong cborUlong:
                    SerializeCborUlong(writer, cbor, objType);
                    break;
                default:
                    break;
            }
            return;
        }

        if (cborType is not null)
        {
            switch (cborType)
            {
                case CborType.Bytes:
                    SerializeCborBytes(writer, cbor, objType);
                    break;
                case CborType.Int:
                    SerializeCborInt(writer, cbor, objType);
                    break;
                case CborType.Ulong:
                    SerializeCborUlong(writer, cbor, objType);
                    break;
                case CborType.List:
                    MethodInfo? method = typeof(CborSerializer).GetMethod(nameof(SerializeList), BindingFlags.NonPublic | BindingFlags.Static);
                    MethodInfo genericMethod = method!.MakeGenericMethod(cbor.GetType().GetGenericArguments());
                    genericMethod.Invoke(null, [writer, cbor, false]);
                    break;
                case CborType.Map:
                    SerializeMap(writer, cbor, objType);
                    break;
                case CborType.Union:
                    SerializeUnion(writer, cbor);
                    break;
                default:
                    throw new NotSupportedException($"CBOR type {cborType} is not supported in this context.");
            }
        }
        else
        {
            switch (objType)
            {
                case Type t when t == typeof(CborInt):
                    SerializeCborInt(writer, cbor, objType);
                    break;
                case Type t when t == typeof(CborBytes):
                    SerializeCborBytes(writer, cbor, objType);
                    break;
                case Type t when t == typeof(CborUlong):
                    SerializeCborUlong(writer, cbor, objType);
                    break;
                case Type t when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(CborList<>):
                    MethodInfo? method = typeof(CborSerializer).GetMethod(nameof(SerializeList), BindingFlags.NonPublic | BindingFlags.Static);
                    MethodInfo genericMethod = method!.MakeGenericMethod(cbor.GetType().GetGenericArguments()[0]);
                    genericMethod.Invoke(null, [writer, cbor, false]);
                    break;
                case Type t when t == typeof(CborMap) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(CborMap<,>)):
                    SerializeMap(writer, cbor, objType);
                    break;
                default:
                    throw new NotImplementedException($"Type not supported {objType}");
            }
        }
    }

    private static void SerializeList<T>(CborWriter writer, CborList<T> cborList, bool indefinite = false) where T : ICbor
    {
        if (!indefinite)
        {
            writer.WriteStartArray(null);
            foreach (var element in cborList.Value)
            {
                SerializeCbor(writer, element, typeof(T));
            }
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteStartArray(cborList.Value.Length);
            foreach (var element in cborList.Value)
            {
                SerializeCbor(writer, element, typeof(T));
            }
            writer.WriteEndArray();
        }
    }

    private static void SerializeCborUlong(CborWriter writer, ICbor cbor, Type targetType)
    {
        if (cbor is CborUlong cborUlong)
        {
            writer.WriteUInt64(cborUlong.Value);
            return;
        }
        throw new InvalidOperationException($"Unsupported target type for Ulong: {targetType.Name}");
    }

    private static void SerializeCborInt(CborWriter writer, ICbor cbor, Type elementType)
    {
        if (cbor is CborInt cborInt)
        {
            writer.WriteInt32(cborInt.Value);
            return;
        }
        throw new InvalidOperationException($"Expected an object of type {typeof(CborInt).Name}, but received {cbor.GetType().Name}.");
    }

    private static void SerializeCborBytes(CborWriter writer, ICbor cbor, Type elementType)
    {
        if (cbor is CborBytes cborBytes)
        {
            writer.WriteByteString(cborBytes.Value);
            return;
        }
        throw new InvalidOperationException($"Expected an object of type {nameof(CborBytes)}, but received {cbor.GetType().Name}.");
    }

    private static void SerializeMap(CborWriter writer, ICbor obj, Type objType)
    {
        try
        {
            if (obj is CborMap cborMap)
            {
                writer.WriteStartMap(cborMap.Value.Count);
                foreach (var kvp in cborMap.Value)
                {
                    SerializeCbor(writer, kvp.Key, kvp.Key.GetType());
                    SerializeCbor(writer, kvp.Value, kvp.Value.GetType());
                }
                writer.WriteEndMap();
            }
            else if (obj is not null && objType.IsGenericType && objType.GetGenericTypeDefinition() == typeof(CborMap<,>))
            {
                var method = typeof(CborSerializer).GetMethod(nameof(SerializeGenericMap), BindingFlags.NonPublic | BindingFlags.Static);
                var genericMethod = method!.MakeGenericMethod(objType.GetGenericArguments());
                genericMethod.Invoke(null, [writer, obj]);
            }
            else if (obj is not null && objType.GetCustomAttribute<CborSerializableAttribute>()?.Type == CborType.Map)
            {
                // SerializeRecordAsMap(writer, obj, objType);
                throw new InvalidOperationException($"Unsupported map type for serialization: {objType.Name}");
            }
            else
            {
                throw new InvalidOperationException($"Unsupported map type for serialization: {objType.Name}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during serialization of type {objType.Name}: {ex.Message}");
        }
    }

    private static void SerializeConstructor(CborWriter writer, ICbor cbor, Type objType)
    {
        var attr = objType.GetCustomAttribute<CborSerializableAttribute>();
        if (attr == null || attr.Index is -1)
        {
            throw new InvalidOperationException($"Type {objType.Name} is not properly attributed for Constructor serialization");
        }

        writer.WriteStartArray(2);
        writer.WriteInt32(attr!.Index);

        if (cbor is Some<ICbor> some)
        {
            SerializeCbor(writer, some.Value, some.Value.GetType());
        }
        else if (cbor is None)
        {
            // None has no additional data
        }
        else
        {
            throw new NotSupportedException($"Unsupported Constructor type: {cbor.GetType().Name}");
        }

        writer.WriteEndArray();
    }

    private static void SerializeRecordAsMap(CborWriter writer, ICbor obj, Type objType)
    {
        var properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        writer.WriteStartMap(properties.Length);

        foreach (var property in properties)
        {
            var attr = property.GetType();
            if (attr != null)
            {
                // if (attr.Name != null)
                // {
                    
                // }
                // else if (attr.Index.HasValue)
                // {
                //     writer.WriteUInt64((ulong)attr.Index.Value);
                // }
                // else
                // {
                //     throw new InvalidOperationException("CborPropertyAttribute requires a Name or Index.");
                // }

                // var value = property.GetValue(obj);

                // if (value is ICbor cborValue)
                // {
                //     SerializeCbor(writer, cborValue, value.GetType());
                // }
                // else
                // {
                //     throw new InvalidOperationException($"Property value of type {value?.GetType().Name} is not a valid ICbor.");
                // }
            }
        }

        writer.WriteEndMap();
    }

    private static void SerializeGenericMap<TKey, TValue>(CborWriter writer, CborMap<TKey, TValue> map)
        where TKey : ICbor
        where TValue : ICbor
    {
        writer.WriteStartMap(map.Value.Count);
        foreach (var kvp in map.Value)
        {
            SerializeCbor(writer, kvp.Key, typeof(TKey));
            SerializeCbor(writer, kvp.Value, typeof(TValue));
        }
        writer.WriteEndMap();
    }

    private static void SerializeUnion(CborWriter writer, ICbor obj)
    {
        var objType = obj.GetType();
        var unionAttr = objType.GetCustomAttribute<CborUnionTypesAttribute>() ??
            throw new InvalidOperationException($"Type {objType.Name} is not marked with CborUnionTypesAttribute");

        var matchedType = unionAttr.UnionTypes.FirstOrDefault(t => t == objType) ??
            throw new InvalidOperationException($"Object of type {objType.Name} does not match any type in the union");
        var index = Array.IndexOf(unionAttr.UnionTypes, matchedType);

        writer.WriteStartMap(2);
        writer.WriteInt32(index);
        SerializeCbor(writer, obj, matchedType);
        writer.WriteEndMap();
    }

    private static ICbor? DeserializeCbor(CborReader reader, Type targetType, CborType? cborType = null)
    {
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        cborType ??= DetermineCborType(targetType);

        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(CborList<>))
        {
            var method = typeof(CborSerializer).GetMethod(nameof(DeserializeList), BindingFlags.NonPublic | BindingFlags.Static);
            var genericMethod = method!.MakeGenericMethod(targetType.GetGenericArguments()[0]);
            return (ICbor?)genericMethod.Invoke(null, [reader, targetType]);
        }

        if (cborType != null)
        {
            return cborType switch
            {
                CborType.Map => DeserializeMap(reader, targetType),
                CborType.Int => DeserializeCborInt(reader, targetType),
                CborType.Ulong => DeserializeCborUlong(reader, targetType),
                CborType.Union => DeserializeUnion(reader, targetType),
                CborType.Bytes => DeserializeCborBytes(reader, targetType),
                CborType.Constr => DeserializeConstructor(reader, targetType),
                _ => throw new NotImplementedException("Unknown CborRepresentation"),
            };
        }

        return targetType switch
        {
            Type t when t == typeof(CborInt) => DeserializeCborInt(reader, targetType),
            Type t when t == typeof(CborBytes) => DeserializeCborBytes(reader, targetType),
            Type t when t == typeof(CborUlong) => DeserializeCborUlong(reader, targetType),
            Type t when t == typeof(CborMap) => DeserializeMap(reader, targetType),
            Type t when t == typeof(CborMap) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(CborMap<,>)) => DeserializeMap(reader, targetType),
            _ => throw new NotImplementedException($"Deserialization not implemented for target type {targetType.Name}"),
        };
    }

    private static CborBytes DeserializeCborBytes(CborReader reader, Type targetType)
    {
        if (targetType != typeof(CborBytes))
        {
            throw new InvalidOperationException($"Expected a target type of {typeof(CborBytes).Name}, but received {targetType.Name}.");
        }

        return new CborBytes(reader.ReadByteString());
    }

    private static CborInt DeserializeCborInt(CborReader reader, Type targetType)
    {
        if (targetType == typeof(CborInt))
        {
            int value = reader.ReadInt32();
            return new CborInt(value);
        }
        throw new InvalidOperationException($"Expected an integer type, but received {targetType.Name}.");
    }

    private static CborUlong DeserializeCborUlong(CborReader reader, Type targetType)
    {
        if (targetType == typeof(CborUlong))
        {
            ulong value = reader.ReadUInt64();
            return new CborUlong(value);
        }
        throw new InvalidOperationException($"Expected a target type of {typeof(CborUlong).Name}, but received {targetType.Name}.");
    }

    private static CborList<T> DeserializeList<T>(CborReader reader, Type targetType) where T : ICbor
    {
        if (reader.PeekState() != CborReaderState.StartArray)
            throw new InvalidOperationException("Expected start of array in CBOR data");

        var itemType = targetType.GetGenericArguments()[0];
        IList<T> list = (IList<T>)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;

        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            var item = (T)DeserializeCbor(reader, itemType)!;
            list.Add(item);
        }
        reader.ReadEndArray();

        return new CborList<T>([.. list]);
    }

    private static ICbor? DeserializeConstructor(CborReader reader, Type targetType)
    {
        reader.ReadStartArray();
        int index = reader.ReadInt32();

        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Option<>))
        {
            var genericArg = targetType.GetGenericArguments()[0];
            if (index == 0)
            {
                var value = DeserializeCbor(reader, genericArg);
                var someType = typeof(Some<>).MakeGenericType(genericArg);
                reader.ReadEndArray();
                return (ICbor?)Activator.CreateInstance(someType, value);
            }
            else if (index == 1)
            {
                reader.ReadEndArray();
                return new None();
            }
        }

        reader.ReadEndArray();
        throw new NotSupportedException($"Unsupported Constructor index: {index} for type {targetType.Name}");
    }

    private static ICbor DeserializeMap(CborReader reader, Type targetType)
    {
        if (targetType == typeof(CborMap))
        {
            return DeserializeCborMap(reader);
        }
        else if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(CborMap<,>))
        {
            var method = typeof(CborSerializer).GetMethod(nameof(DeserializeGenericMap), BindingFlags.NonPublic | BindingFlags.Static);
            var genericMethod = method!.MakeGenericMethod(targetType.GetGenericArguments());
            return (ICbor)genericMethod.Invoke(null, [reader])!;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported map type for deserialization: {targetType.Name}");
        }
    }

    private static CborMap DeserializeCborMap(CborReader reader)
    {
        reader.ReadStartMap();
        var dictionary = new Dictionary<ICbor, ICbor>();

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = DeserializeCbor(reader, typeof(ICbor))!;
            var value = DeserializeCbor(reader, typeof(ICbor))!;
            dictionary.Add(key, value);
        }

        reader.ReadEndMap();
        return new CborMap(dictionary);
    }

    private static ICbor? DeserializeUnion(CborReader reader, Type targetType)
    {
        var unionAttr = targetType.GetCustomAttribute<CborUnionTypesAttribute>() ??
            throw new InvalidOperationException($"Type {targetType.Name} is not marked with CborUnionTypesAttribute");

        foreach (var type in unionAttr.UnionTypes)
        {
            try
            {
                var result = DeserializeCbor(reader, type);
                return result;
            }
            catch
            {
                continue;
            }
        }

        throw new InvalidOperationException("No matching type found in the union");
    }

    private static CborMap<TKey, TValue> DeserializeGenericMap<TKey, TValue>(CborReader reader)
        where TKey : ICbor
        where TValue : ICbor
    {
        reader.ReadStartMap();
        var dictionary = new Dictionary<TKey, TValue>();

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = (TKey)DeserializeCbor(reader, typeof(TKey))!;
            var value = (TValue)DeserializeCbor(reader, typeof(TValue))!;
            dictionary.Add(key, value);
        }

        reader.ReadEndMap();
        return new CborMap<TKey, TValue>(dictionary);
    }

    private static CborType DetermineCborType(Type objType)
    {
        if (typeof(ICbor).IsAssignableFrom(objType))
        {
            var attr = objType.GetCustomAttribute<CborSerializableAttribute>();
            if (attr != null)
            {
                return attr.Type;
            }

            if (objType == typeof(CborBytes)) return CborType.Bytes;
            if (objType == typeof(CborInt)) return CborType.Int;
            if (objType == typeof(CborUlong)) return CborType.Ulong;

            if (objType.IsGenericType && objType.GetGenericTypeDefinition() == typeof(CborList<>))
            {
                return CborType.List;
            }

            throw new NotSupportedException($"The ICbor type {objType.Name} is not supported for serialization.");
        }

        // Check for union types
        if (objType.GetCustomAttribute<CborUnionTypesAttribute>() != null)
        {
            return CborType.Union;
        }

        // Check for collections
        if (typeof(IEnumerable).IsAssignableFrom(objType) && objType.IsGenericType)
        {
            Type genericType = objType.GetGenericTypeDefinition();
            if (genericType == typeof(List<>) || genericType == typeof(IEnumerable<>))
            {
                return CborType.List;
            }

            if (genericType == typeof(CborList<>))
            {
                return CborType.List;
            }
        }

        // Check for dictionaries
        if (typeof(IDictionary).IsAssignableFrom(objType) && objType.IsGenericType)
        {
            if (objType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                return CborType.Map;
            }

        }

        // Check specific types
        if (objType == typeof(CborMap)) return CborType.Map;
        if (objType == typeof(CborInt)) return CborType.Int;
        if (objType == typeof(CborBytes)) return CborType.Bytes;
        if (objType == typeof(CborUlong)) return CborType.Ulong;

        throw new NotSupportedException($"The object type {objType.Name} is not supported for serialization.");
    }

    private static ICbor? TryMatchType(CborReader reader, Type type)
    {
        try
        {
            var result = DeserializeCbor(reader, type);
            return result;
        }
        catch
        {
            reader.SkipValue();
            return null;
        }
    }


    public static string ToHex(ICbor cbor)
    {
        byte[] cborData = Serialize(cbor);
        return Convert.ToHexString(cborData).ToLowerInvariant();
    }

    public static T? FromHex<T>(string hexString)
    {
        byte[] cborData = Convert.FromHexString(hexString);
        return Deserialize<T>(cborData);
    }
}