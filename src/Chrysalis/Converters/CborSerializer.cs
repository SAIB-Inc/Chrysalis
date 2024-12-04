using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public static class CborSerializer
{
    private static readonly Dictionary<Type, object> _converterCache = new();

    public static byte[] Serialize<T>(T value) where T : ICbor
    {
        ICborConverter<T> converter = GetConverter<T>(value.GetType());
        return converter.Serialize(value);
    }

    public static T Deserialize<T>(byte[] data) where T : ICbor
    {
        ICborConverter<T> converter = GetConverter<T>(typeof(T));
        return (T)converter.Deserialize(data);
    }

    private static ICborConverter<T> GetConverter<T>(Type type) where T : ICbor
    {
        if (_converterCache.TryGetValue(type, out object? cached))
        {
            return (ICborConverter<T>)cached;
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
                    return (ICborConverter<T>)converter;
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
                        return (ICborConverter<T>)converter;
                    }
                }
                else
                {
                    object? converter = Activator.CreateInstance(converterType);
                    if (converter != null)
                    {
                        _converterCache[type] = converter;
                        return (ICborConverter<T>)converter;
                    }
                }
            }
            currentType = currentType.BaseType;
        }

        throw new InvalidOperationException($"No valid converter found for {type}");
    }
}