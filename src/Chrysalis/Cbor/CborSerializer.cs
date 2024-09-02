using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cardano.Models;
using Chrysalis.Utils;

namespace Chrysalis.Cbor;
public static class CborSerializer
{
    public static byte[] Serialize(ICbor cbor)
    {
        CborWriter writer = new(CborConformanceMode.Strict);
        SerializeCbor(writer, cbor, cbor.GetType());
        return writer.Encode();
    }

    public static T? Deserialize<T>(byte[] cborData)
    {
        CborReader reader = new(cborData, CborConformanceMode.Strict);
        return (T?)DeserializeCbor(reader, typeof(T), cborData);
    }

    private static void SerializeCbor(CborWriter writer, ICbor cbor, Type objType)
    {
        CborType? cborType = CborSerializerUtils.GetCborType(objType);

        if (cborType is not null)
        {
            CborSerializableAttribute? attr = objType.GetCustomAttribute<CborSerializableAttribute>();
            switch (cborType)
            {
                case CborType.Bytes:
                    SerializeCborBytes(writer, cbor, objType, attr!.IsIndefinite);
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
                    genericMethod.Invoke(null, [writer, cbor, attr?.IsIndefinite]);
                    break;
                case CborType.Map:
                    SerializeMap(writer, cbor, objType, attr!.IsIndefinite);
                    break;
                case CborType.Constr:
                    SerializeConstructor(writer, cbor, objType);
                    break;
                default:
                    throw new NotSupportedException($"CBOR type {cborType} is not supported in this context.");
            }
        }
        else
        {
            throw new NotImplementedException($"Type not supported {objType}");
        }
    }

    private static void SerializeList<T>(CborWriter writer, CborList<T> cborList, bool indefinite = false) where T : ICbor
    {
        writer.WriteStartArray(indefinite ? null : cborList.Value.Length);

        foreach (T element in cborList.Value)
        {
            SerializeCbor(writer, element, typeof(T));
        }

        writer.WriteEndArray();
    }

    private static void SerializeCborUlong(CborWriter writer, ICbor cbor, Type targetType)
    {
        writer.WriteUInt64((ulong)cbor.GetValue(targetType));
    }

    private static void SerializeCborInt(CborWriter writer, ICbor cbor, Type elementType)
    {
        writer.WriteInt32((int)cbor.GetValue(elementType));
    }

    private static void SerializeCborBytes(CborWriter writer, ICbor cbor, Type elementType, bool indefinite = false)
    {
        if (indefinite)
        {
            writer.WriteStartIndefiniteLengthByteString();
            byte[] bytesValue = (byte[])cbor.GetValue(elementType);
            const int chunkSize = 1024;
            for (int i = 0; i < bytesValue.Length; i += chunkSize)
            {
                int length = Math.Min(chunkSize, bytesValue.Length - i);
                writer.WriteByteString(((byte[])cbor.GetValue(elementType)).AsSpan(i, length));
            }
            writer.WriteEndIndefiniteLengthByteString();
        }
        else
        {
            writer.WriteByteString((byte[])cbor.GetValue(elementType));
        }
    }

    private static void SerializeMap(CborWriter writer, ICbor obj, Type objType, bool indefinite = false)
    {
        try
        {
            if (obj is CborMap cborMap)
            {
                writer.WriteStartMap(indefinite ? null : cborMap.Value.Count);
                foreach (KeyValuePair<ICbor, ICbor> kvp in cborMap.Value)
                {
                    SerializeCbor(writer, kvp.Key, kvp.Key.GetType());
                    SerializeCbor(writer, kvp.Value, kvp.Value.GetType());
                }
                writer.WriteEndMap();
            }
            else if (obj is not null && objType.IsGenericType && objType.GetGenericTypeDefinition() == typeof(CborMap<,>))
            {
                MethodInfo? method = typeof(CborSerializer).GetMethod(nameof(SerializeGenericMap), BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo genericMethod = method!.MakeGenericMethod(objType.GetGenericArguments());
                genericMethod.Invoke(null, [writer, obj, indefinite]);
            }
            else if (obj is not null && objType.GetCustomAttribute<CborSerializableAttribute>()?.Type == CborType.Map)
            {
                // Not yet tested
                SerializeRecordAsMap(writer, obj, objType);
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

    // Todo
    private static void SerializeRecordAsMap(CborWriter writer, ICbor obj, Type objType, bool indefinite = false)
    {
        PropertyInfo[] properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        writer.WriteStartMap(indefinite ? null : properties.Length);

        foreach (PropertyInfo property in properties)
        {
            Type attr = property.GetType();
            if (attr != null)
            {
                CborPropertyAttribute? cborPropertyAttr = property.GetCustomAttribute<CborPropertyAttribute>();
                string key = cborPropertyAttr?.Key ?? property.Name;
                object? value = property.GetValue(obj);

                writer.WriteTextString(key);
                SerializeCbor(writer, (ICbor)value!, property.PropertyType);
            }
        }

        writer.WriteEndMap();
    }

    private static void SerializeConstructor(CborWriter writer, ICbor cbor, Type objType)
    {
        CborSerializableAttribute? cborSerializableAttr = objType.GetCustomAttribute<CborSerializableAttribute>();
        if (cborSerializableAttr == null || cborSerializableAttr.Type != CborType.Constr)
        {
            throw new InvalidOperationException($"Type {objType.Name} is not marked as CborSerializable with CborType.Constr");
        }

        ConstructorInfo? constructor = objType.GetConstructors().FirstOrDefault()
            ?? throw new InvalidOperationException($"No constructor found for type {objType.Name}");

        ParameterInfo[] parameters = constructor.GetParameters();

        writer.WriteTag(CborSerializerUtils.GetCborTag(cborSerializableAttr.Index));
        if (parameters.Length == 0)
        {
            writer.WriteStartArray(0);
            writer.WriteEndArray();
            return;
        }
        writer.WriteStartArray(null);

        foreach (ParameterInfo parameter in parameters)
        {
            PropertyInfo? property = objType.GetProperty(parameter.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new InvalidOperationException($"Property {parameter.Name} not found in {objType.Name}");

            object? value = property.GetValue(cbor)
                ?? throw new InvalidOperationException($"Value for property {property.Name} in {objType.Name} is null");

            SerializeCbor(writer, (ICbor)value, value.GetType());
        }

        writer.WriteEndArray();
    }

    private static void SerializeGenericMap<TKey, TValue>(CborWriter writer, CborMap<TKey, TValue> map, bool indefinite = false)
        where TKey : ICbor
        where TValue : ICbor
    {
        writer.WriteStartMap(indefinite ? null : map.Value.Count);
        foreach (KeyValuePair<TKey, TValue> kvp in map.Value)
        {
            SerializeCbor(writer, kvp.Key, typeof(TKey));
            SerializeCbor(writer, kvp.Value, typeof(TValue));
        }
        writer.WriteEndMap();
    }

    private static ICbor? DeserializeCbor(CborReader reader, Type targetType, byte[]? cborData = null)
    {
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        CborType? cborType = CborSerializerUtils.GetCborType(targetType);

        if (cborType == CborType.List)
        {
            MethodInfo? method = typeof(CborSerializer).GetMethod(nameof(DeserializeList), BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo genericMethod = method!.MakeGenericMethod(targetType.GetGenericArguments()[0]);
            return (ICbor?)genericMethod.Invoke(null, [reader, targetType]);
        }

        if (cborType != null)
        {
            return cborType switch
            {
                CborType.Map => DeserializeMap(reader, targetType),
                CborType.Int => DeserializeCborInt(reader, targetType),
                CborType.Ulong => DeserializeCborUlong(reader, targetType),
                CborType.Union => DeserializeUnion(reader, cborData!, targetType),
                CborType.Bytes => DeserializeCborBytes(reader, targetType),
                CborType.Constr => DeserializeConstructor(reader, targetType),
                _ => throw new NotImplementedException($"Unknown CborType: {cborType}"),
            };
        }

        throw new NotImplementedException($"Deserialization not implemented for target type {targetType.Name}");
    }

    private static CborBytes DeserializeCborBytes(CborReader reader, Type targetType)
    {
        byte[] value = reader.ReadByteString();
        return (CborBytes)Activator.CreateInstance(targetType, value)!;
    }

    private static CborInt DeserializeCborInt(CborReader reader, Type targetType)
    {
        int value = reader.ReadInt32();
        return (CborInt)Activator.CreateInstance(targetType, value)!;
    }

    private static CborUlong DeserializeCborUlong(CborReader reader, Type targetType)
    {
        ulong value = reader.ReadUInt64();
        return (CborUlong)Activator.CreateInstance(targetType, value)!;
    }

    private static CborList<T> DeserializeList<T>(CborReader reader, Type targetType) where T : ICbor
    {
        if (reader.PeekState() != CborReaderState.StartArray)
            throw new InvalidOperationException("Expected start of array in CBOR data");

        Type itemType = targetType.GetGenericArguments()[0];
        IList<T> list = (IList<T>)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;

        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            T item = (T)DeserializeCbor(reader, itemType)!;
            list.Add(item);
        }
        reader.ReadEndArray();

        return new CborList<T>([.. list]);
    }

    private static ICbor? DeserializeConstructor(CborReader reader, Type targetType)
    {
        reader.ReadTag();
        reader.ReadStartArray();

        if (targetType.IsGenericType)
        {
            Type genericTypeDef = targetType.GetGenericTypeDefinition();
            Type[] genericArgs = targetType.GetGenericArguments();

            CborSerializableAttribute? cborSerializableAttr = genericTypeDef.GetCustomAttribute<CborSerializableAttribute>();
            if (cborSerializableAttr != null)
            {
                ParameterInfo[] constructorParams = targetType.GetConstructors().First().GetParameters();
                object?[] args = new object?[constructorParams.Length];

                for (int i = 0; i < constructorParams.Length; i++)
                {
                    Type paramType = constructorParams[i].ParameterType;
                    args[i] = DeserializeCbor(reader, paramType);
                }

                reader.ReadEndArray();
                return (ICbor?)Activator.CreateInstance(targetType, args);
            }
            else
            {
                if (genericArgs.Length == 1)
                {
                    Type genericArg = genericArgs[0];
                    ICbor? value = DeserializeCbor(reader, genericArg);
                    reader.ReadEndArray();
                    return (ICbor?)Activator.CreateInstance(targetType, value);
                }

                throw new InvalidOperationException($"Unhandled generic type: {genericTypeDef.Name}");
            }
        }
        else
        {
            CborSerializableAttribute? cborSerializableAttr = targetType.GetCustomAttribute<CborSerializableAttribute>();
            if (cborSerializableAttr == null || cborSerializableAttr.Type != CborType.Constr)
            {
                throw new InvalidOperationException($"Type {targetType.Name} is not marked as CborSerializable with CborType.Constr");
            }

            ParameterInfo[] constructorParams = targetType.GetConstructors().First().GetParameters();
            object?[] args = new object?[constructorParams.Length];

            for (int i = 0; i < constructorParams.Length; i++)
            {
                Type paramType = constructorParams[i].ParameterType;
                args[i] = DeserializeCbor(reader, paramType);
            }

            reader.ReadEndArray();
            return (ICbor?)Activator.CreateInstance(targetType, args);
        }
    }

    private static ICbor DeserializeMap(CborReader reader, Type targetType)
    {
        if (targetType == typeof(CborMap))
        {
            return DeserializeCborMap(reader);
        }
        else if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(CborMap<,>))
        {
            MethodInfo? method = typeof(CborSerializer).GetMethod(nameof(DeserializeGenericMap), BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo genericMethod = method!.MakeGenericMethod(targetType.GetGenericArguments());
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
        Dictionary<ICbor, ICbor> dictionary = [];

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            ICbor key = DeserializeCbor(reader, typeof(ICbor))!;
            ICbor value = DeserializeCbor(reader, typeof(ICbor))!;
            dictionary.Add(key, value);
        }

        reader.ReadEndMap();
        return new CborMap(dictionary);
    }

    private static ICbor? DeserializeUnion(CborReader reader, byte[] cborData, Type targetType)
    {
        CborUnionTypesAttribute unionAttr = targetType.GetCustomAttribute<CborUnionTypesAttribute>() ??
            throw new InvalidOperationException($"Type {targetType.Name} is not marked with CborUnionTypesAttribute");

        foreach (Type type in unionAttr.UnionTypes)
        {

            try
            {
                Type? unionType = type.IsGenericTypeDefinition ? type.MakeGenericType(targetType.GetGenericArguments()) : type;
                return DeserializeCbor(reader, unionType);
            }
            catch
            {
                reader.Reset(cborData);
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
        Dictionary<TKey, TValue> dictionary = [];

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            TKey key = (TKey)DeserializeCbor(reader, typeof(TKey))!;
            TValue value = (TValue)DeserializeCbor(reader, typeof(TValue))!;
            dictionary.Add(key, value);
        }

        reader.ReadEndMap();
        return new CborMap<TKey, TValue>(dictionary);
    }
}