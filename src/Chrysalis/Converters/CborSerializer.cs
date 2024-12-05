using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public static class CborSerializer
{
    private static readonly Dictionary<Type, object> _converterCache = [];

    public static byte[] Serialize<T>(T value) where T : Cbor
    {
        ICborConverter converter = GetConverter<T>(value.GetType());
        return converter.Serialize(value);
    }

    public static T Deserialize<T>(byte[] data) where T : Cbor
    {
        Type? targetType = typeof(T);
        ICborConverter converter = GetConverter<T>(targetType);

        // Find the `Deserialize` method
        MethodInfo deserializeMethod = typeof(ICborConverter).GetMethod(nameof(ICborConverter.Deserialize))
            ?? throw new InvalidOperationException("The ICborConverter does not implement Deserialize.");

        // Make the method generic with the targetType
        MethodInfo genericMethod = deserializeMethod.MakeGenericMethod(targetType);

        // Dynamically invoke Deserialize<T>(data)
        object? result = genericMethod.Invoke(converter, [data]);

        if (result is T typedResult)
        {
            return typedResult;
        }

        throw new InvalidOperationException($"Failed to cast deserialized result to {typeof(T).Name}.");
    }

    private static ICborConverter GetConverter<T>(Type type) where T : ICbor
    {
        if (_converterCache.TryGetValue(type, out object? cached))
        {
            return (ICborConverter)cached;
        }

        // If we're dealing with a derived type like CborDefList<T>
        if (type.BaseType?.GetCustomAttribute<CborConverterAttribute>() != null)
        {
            // Use the base type's converter but with the derived type
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
                    // Get the generic args from the type where we found the converter
                    Type[] genericArgs = type.GenericTypeArguments;
                    Type constructedType = converterType.MakeGenericType(genericArgs);
                    object? converter = Activator.CreateInstance(constructedType);

                    // Explicitly cast to ICborConverter<T>
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