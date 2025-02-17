using System.Collections.Concurrent;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Utils.Exceptions;

namespace Chrysalis.Cbor.Utils;

internal static class ConverterUtils
{
    private static readonly ConcurrentDictionary<Type, ICborConverter> ConverterCache = new();

    public static ICborConverter CreateConverter(Type? converterType)
    {
        if (converterType == null)
            throw new RegistryException("Converter type cannot be null");

        return ConverterCache.GetOrAdd(converterType, t =>
        {
            if (!typeof(ICborConverter).IsAssignableFrom(t))
                throw new RegistryException($"Type {t} must implement ICborConverter");

            return (ICborConverter)Activator.CreateInstance(t)!;
        });
    }
}