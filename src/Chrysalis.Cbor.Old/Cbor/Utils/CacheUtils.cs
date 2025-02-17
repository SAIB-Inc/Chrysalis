using System.Collections.Concurrent;
using Chrysalis.Cbor.Utils.Exceptions;

namespace Chrysalis.Cbor.Utils;


internal static class CacheUtils
{
    public static TValue GetOrAdd<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> cache,
        TKey key,
        Func<TKey, TValue> valueFactory) where TKey : notnull
    {
        try
        {
            return cache.GetOrAdd(key, k =>
            {
                try
                {
                    return valueFactory(k);
                }
                catch (Exception ex)
                {
                    throw new RegistryException($"Error creating value for key {k}", ex);
                }
            });
        }
        catch (Exception ex)
        {
            throw new RegistryException($"Cache operation failed for key {key}", ex);
        }
    }
}