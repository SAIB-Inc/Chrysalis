// Utils/Cache/TypeMetadataCache.cs
using System.Collections.Concurrent;
using System.Reflection;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils.Exceptions;

namespace Chrysalis.Cbor.Utils.Cache;

internal sealed class TypeMetadataCache
{
    private readonly ConcurrentDictionary<Type, Lazy<CborOptions>> _optionsCache = new();
    private readonly ConcurrentDictionary<Type, Lazy<PropertyInfo[]>> _propertiesCache = new();
    private readonly ConcurrentDictionary<(Type Type, string Name), Lazy<Delegate>> _delegateCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    internal CborOptions GetOrCreateOptions(Type type, Func<Type, CborOptions> optionsFactory)
    {
        return _optionsCache.GetOrAdd(type, t =>
            new Lazy<CborOptions>(() => optionsFactory(t), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    internal PropertyInfo[] GetOrCreateProperties(Type type, Func<Type, PropertyInfo[]> propertyFactory)
    {
        return _propertiesCache.GetOrAdd(type, t =>
            new Lazy<PropertyInfo[]>(() => propertyFactory(t), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    internal Delegate GetOrCreateDelegate(Type type, string name, Func<Type, string, Delegate> delegateFactory)
    {
        return _delegateCache.GetOrAdd((type, name), key =>
            new Lazy<Delegate>(() => delegateFactory(key.Type, key.Name), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    internal void Clear()
    {
        _optionsCache.Clear();
        _propertiesCache.Clear();
        _delegateCache.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            Clear();
            _cacheLock.Dispose();
        }
        catch (Exception ex)
        {
            throw new RegistryException("Error disposing cache", ex);
        }
    }
}