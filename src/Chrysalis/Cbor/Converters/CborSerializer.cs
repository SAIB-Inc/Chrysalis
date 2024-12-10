using System.Collections.Concurrent;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public static class CborSerializer
{
    private static readonly Dictionary<Type, object> _converterCache = [];
    private static readonly ConcurrentDictionary<Type, Func<byte[], object>> _deserializeDelegates = new();

    public static byte[] Serialize<T>(T value) where T : CborBase
    {
        ICborConverter converter = GetConverter<T>(value.GetType());
        return converter.Serialize(value);
    }

    public static T Deserialize<T>(byte[] data) where T : CborBase
    {
        Type targetType = typeof(T);

        // Use the delegate cache to retrieve or create the deserialization logic
        Func<byte[], object> deserializer = _deserializeDelegates.GetOrAdd(targetType, type =>
        {
            ICborConverter converter = GetConverter<T>(type);

            // Use reflection to create the specific Deserialize<T> method
            MethodInfo deserializeMethod = typeof(ICborConverter).GetMethod(nameof(ICborConverter.Deserialize))
                ?.MakeGenericMethod(type) ?? throw new InvalidOperationException("The ICborConverter does not implement Deserialize.");

            return (byte[] inputData) =>
            {
                return deserializeMethod.Invoke(converter, new object[] { inputData })!;
            };
        });

        // Call the cached delegate
        return (T)deserializer(data);
    }

    private static ICborConverter GetConverter<T>(Type type) where T : ICbor
    {
        if (_converterCache.TryGetValue(type, out object? cached))
        {
            return (ICborConverter)cached;
        }

        // Check if the type has a base type with a converter attribute
        if (type.BaseType?.GetCustomAttribute<CborConverterAttribute>() != null)
        {
            Type baseType = type.BaseType;
            CborConverterAttribute? converterAttr = baseType.GetCustomAttribute<CborConverterAttribute>();
            Type? converterType = converterAttr?.ConverterType;

            if (converterType is not null && converterType.IsGenericTypeDefinition)
            {
                Type[] genericArgs = type.GenericTypeArguments;
                Type constructedConverter = converterType.MakeGenericType(genericArgs);
                object? converter = Activator.CreateInstance(constructedConverter);

                if (converter != null)
                {
                    _converterCache[type] = converter;
                    return (ICborConverter)converter;
                }
            }
        }

        Type? currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            CborConverterAttribute? converterAttr = currentType.GetCustomAttribute<CborConverterAttribute>();
            if (converterAttr != null)
            {
                Type converterType = converterAttr.ConverterType;

                if (converterType.IsGenericTypeDefinition)
                {
                    Type[] genericArgs = type.GenericTypeArguments;
                    Type constructedType = converterType.MakeGenericType(genericArgs);
                    object? converter = Activator.CreateInstance(constructedType);

                    if (converter != null)
                    {
                        _converterCache[type] = converter;
                        return (ICborConverter)converter;
                    }
                }
                else
                {
                    object? converter = Activator.CreateInstance(converterType);
                    if (converter != null)
                    {
                        _converterCache[type] = converter;
                        return (ICborConverter)converter;
                    }
                }
            }
            currentType = currentType.BaseType;
        }

        throw new InvalidOperationException($"No valid converter found for {type}");
    }
}