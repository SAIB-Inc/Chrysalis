// Registry.cs
using System.Reflection;
using Chrysalis.Cbor.CacheRegistry;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;
using Chrysalis.Cbor.Utils.Exceptions;
public class Registry : IRegistry, IAsyncDisposable
{
    private readonly RegistryStorage _storage = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private int _initialized;

    public void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1) return;

        // First register converters - create single instances
        var converters = TypeUtils.DiscoverConverters();
        foreach (var converterType in converters)
        {
            var converter = (ICborConverter)Activator.CreateInstance(converterType)!;
            _storage.Converters.TryAdd(converterType, converter);
        }

        // Then register CBOR types
        var types = TypeUtils.DiscoverCborTypes();
        var unionTypes = TypeUtils.FindUnionTypes(types);

        foreach (var type in types)
        {
            RegisterType(type, unionTypes);
        }
    }

    private void RegisterType(Type type, Dictionary<Type, List<Type>> unionTypes)
    {
        var options = OptionsUtils.CreateOptions(type, unionTypes);
        _storage.Options.TryAdd(type.IsGenericType ? type.GetGenericTypeDefinition() : type, options);

        if (!type.IsAbstract)
        {
            RegisterMembers(type);
        }
    }

    public ICborConverter GetConverter(Type type)
    {
        EnsureInitialized();
        return _storage.Converters.GetOrAdd(type,
            t => (ICborConverter)Activator.CreateInstance(t)!);
    }

    public ConstructorInfo[] GetConstructors(Type type)
    {
        EnsureInitialized();
        return type.GetConstructors();
    }

    public MethodInfo GetMethod(Type type, string methodName)
    {
        EnsureInitialized();
        return type.GetMethod(methodName)
            ?? throw new RegistryException($"Method {methodName} not found on type {type}");
    }

    private void RegisterMembers(Type type)
    {
        var properties = PropertyUtils.GetCborProperties(type);
        _storage.Properties.TryAdd(type, properties);

        foreach (var prop in properties)
        {
            if (prop.CanRead)
                _storage.Getters.TryAdd((type, prop.Name), ExpressionUtils.CreateGetter(type, prop));
            if (prop.CanWrite)
                _storage.Setters.TryAdd((type, prop.Name), ExpressionUtils.CreateSetter(type, prop));
        }
    }

    public CborOptions GetOptions(Type type)
    {
        EnsureInitialized();

        // Try to get options for the exact type
        var normalizedType = type.IsGenericType ? type.GetGenericTypeDefinition() : type;

        if (_storage.Options.TryGetValue(normalizedType, out var options))
        {
            return options;
        }

        // If not found, walk up the inheritance chain
        var currentType = type.BaseType;
        while (currentType != null && currentType != typeof(object))
        {
            normalizedType = currentType.IsGenericType ?
                currentType.GetGenericTypeDefinition() : currentType;

            if (_storage.Options.TryGetValue(normalizedType, out options))
            {
                // Create a new options instance for the derived type
                return new CborOptions(
                    Index: options.Index,
                    ConverterType: options.ConverterType,
                    IsDefinite: options.IsDefinite,
                    IsUnion: options.IsUnion,
                    ActivatorType: type, // Use the actual type
                    Size: options.Size,
                    Tag: options.Tag,
                    PropertyNameTypes: options.PropertyNameTypes,
                    PropertyIndexTypes: options.PropertyIndexTypes,
                    UnionTypes: options.UnionTypes
                );
            }

            currentType = currentType.BaseType;
        }

        // If no options found in hierarchy, create default options
        return _storage.Options.GetOrAdd(normalizedType,
            static t => OptionsUtils.CreateOptions(t, []));
    }

    public Delegate GetActivator(Type type)
    {
        EnsureInitialized();
        return _storage.Activators.GetOrAdd(type, ExpressionUtils.CreateActivator);
    }

    public PropertyInfo[] GetProperties(Type type)
    {
        EnsureInitialized();
        return _storage.Properties.GetOrAdd(type, PropertyUtils.GetCborProperties);
    }

    public Delegate GetGetter(Type type, string propertyName)
    {
        EnsureInitialized();
        return _storage.Getters.GetOrAdd((type, propertyName),
            static k => ExpressionUtils.CreateGetter(k.Type, k.Type.GetProperty(k.Name)!));
    }

    public Delegate GetSetter(Type type, string propertyName)
    {
        EnsureInitialized();
        return _storage.Setters.GetOrAdd((type, propertyName),
            static k => ExpressionUtils.CreateSetter(k.Type, k.Type.GetProperty(k.Name)!));
    }

    private void EnsureInitialized() => Initialize();

    public async ValueTask DisposeAsync()
    {
        await _initLock.WaitAsync();
        try { _initLock.Dispose(); }
        finally { _storage.Clear(); }
    }
}