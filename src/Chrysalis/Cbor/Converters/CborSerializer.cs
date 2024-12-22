using System.Collections.Concurrent;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public static class CborSerializer
{
    private static readonly ConcurrentDictionary<Type, object> _converterCache = [];
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
                return deserializeMethod.Invoke(converter, [inputData])!;
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

        // First check if the type itself has a converter attribute
        CborConverterAttribute? converterAttr = type.GetCustomAttribute<CborConverterAttribute>();

        if (converterAttr == null)
        {
            // If not found on type, check base types
            Type? currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                converterAttr = currentType.GetCustomAttribute<CborConverterAttribute>();
                if (converterAttr != null)
                    break;
                currentType = currentType.BaseType;
            }
        }

        if (converterAttr != null)
        {
            Type converterType = converterAttr.ConverterType;
            object? converter;

            if (converterType.IsGenericTypeDefinition)
            {
                // Handle generic converters
                Type[] genericArgs;
                if (type.IsGenericType)
                {
                    genericArgs = type.GetGenericArguments();
                }
                else
                {
                    // If the type isn't generic but converter is, use T as the generic argument
                    genericArgs = [typeof(T)];
                }

                try
                {
                    Type constructedType = converterType.MakeGenericType(genericArgs);
                    converter = Activator.CreateInstance(constructedType);
                }
                catch (ArgumentException)
                {
                    // If we can't construct with the type's generic arguments, try with just T
                    Type constructedType = converterType.MakeGenericType(typeof(T));
                    converter = Activator.CreateInstance(constructedType);
                }
            }
            else
            {
                converter = Activator.CreateInstance(converterType);
            }

            if (converter != null)
            {
                _converterCache[type] = converter;
                return (ICborConverter)converter;
            }
        }

        // Additional fallback for generic types
        if (type.IsGenericType)
        {
            Type genericDefinition = type.GetGenericTypeDefinition();
            if (_converterCache.TryGetValue(genericDefinition, out object? genericConverter))
            {
                _converterCache[type] = genericConverter;
                return (ICborConverter)genericConverter;
            }
        }

        throw new InvalidOperationException(
            $"No valid converter found for {type}. Please ensure a [CborConverter] attribute is defined.");
    }

    public static CborReader CreateReader(byte[] data)
    {
        return new(data, CborConformanceMode.Lax);
    }
}