using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cache;

public class ChrysalisRegistry
{
    private readonly ConcurrentDictionary<Type, ICborConverter> _converters = new();
    private readonly ConcurrentDictionary<Type, CborOptions> _options = new();
    private readonly ConcurrentDictionary<Type, Delegate> _activators = new();
    private readonly ConcurrentDictionary<Type, PropertyInfo[]> _properties = new();
    private readonly ConcurrentDictionary<(Type, string), Delegate> _getters = new();
    private readonly ConcurrentDictionary<(Type, string), Delegate> _setters = new();
    private readonly ConcurrentDictionary<Type, ConstructorInfo[]> _constructors = new();
    private readonly ConcurrentDictionary<(Type, string), MethodInfo> _methods = new();

    public ConcurrentDictionary<Type, CborOptions> GetOptions()
    {
        return _options;
    }

    public ICborConverter GetConverter(Type type)
    {
        Type normalizedType = NormalizeType(type);
        return _converters.TryGetValue(normalizedType, out ICborConverter? converter)
            ? converter
            : throw new InvalidOperationException($"No converter registered for type {type}");
    }

    public CborOptions GetOptions(Type type)
    {
        Type normalizedType = NormalizeType(type);
        if (_options.TryGetValue(normalizedType, out CborOptions? options))
        {
            options.ActivatorType = type;
            return options;
        }
        throw new InvalidOperationException($"No options registered for type {type}");
    }

    public Delegate GetActivator(Type type)
    {
        // Direct lookup first
        if (_activators.TryGetValue(type, out Delegate? activator))
            return activator;

        // If it's a closed generic, see if we need to create its activator
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            // Create and cache the activator for this closed generic
            ConstructorInfo? ctor = type.GetConstructors().FirstOrDefault();
            if (ctor != null)
            {
                ParameterInfo[] parameters = ctor.GetParameters();
                if (parameters.Length == 0)
                {
                    LambdaExpression expr = Expression.Lambda(Expression.New(ctor));
                    activator = expr.Compile();
                }
                else
                {
                    ParameterExpression[] paramExprs = parameters
                        .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                        .ToArray();
                    LambdaExpression expr = Expression.Lambda(
                        Expression.New(ctor, paramExprs),
                        paramExprs);
                    activator = expr.Compile();
                }

                _activators.TryAdd(type, activator);
                return activator;
            }
        }

        throw new InvalidOperationException($"No activator registered for type {type}");
    }

    public PropertyInfo[] GetProperties(Type type)
    {
        Type normalizedType = NormalizeType(type);
        return _properties.TryGetValue(normalizedType, out PropertyInfo[]? properties)
            ? properties
            : throw new InvalidOperationException($"No properties registered for type {type}");
    }

    public Delegate GetGetter(Type type, string propertyName)
    {
        Type normalizedType = NormalizeType(type);
        return _getters.TryGetValue((normalizedType, propertyName), out Delegate? getter)
            ? getter
            : throw new InvalidOperationException($"No getter registered for property {propertyName} on type {type}");
    }

    public Delegate GetSetter(Type type, string propertyName)
    {
        Type normalizedType = NormalizeType(type);
        return _setters.TryGetValue((normalizedType, propertyName), out Delegate? setter)
            ? setter
            : throw new InvalidOperationException($"No setter registered for property {propertyName} on type {type}");
    }

    public ConstructorInfo[] GetConstructors(Type type)
    {
        Type normalizedType = NormalizeType(type);
        return _constructors.TryGetValue(normalizedType, out ConstructorInfo[]? ctors)
            ? ctors
            : throw new InvalidOperationException($"No constructors registered for type {type}");
    }

    public MethodInfo GetMethod(Type type, string methodName)
    {
        Type normalizedType = NormalizeType(type);
        return _methods.TryGetValue((normalizedType, methodName), out MethodInfo? method)
            ? method
            : throw new InvalidOperationException($"No method registered for name {methodName} on type {type}");
    }

    public void InitializeRegistry()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        try
        {
            RegisterConverters(assembly);
            RegisterCborTypes(assembly);
            RegisterGettersAndSetters();
            RegisterPropertyInfo();
            RegisterConstructors();
            RegisterMethods();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private void RegisterConverters(Assembly assembly)
    {
        IEnumerable<Type> converterTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ICborConverter).IsAssignableFrom(t));

        foreach (Type? type in converterTypes)
        {
            if (Activator.CreateInstance(type) is ICborConverter converter)
            {
                _converters.TryAdd(type, converter);
            }
        }
    }

    private void RegisterCborTypes(Assembly assembly)
    {
        var allTypes = assembly.GetTypes();
        var cborTypes = new HashSet<Type>();

        // Discover all CBOR types including those used in constructors and properties
        foreach (var type in allTypes)
        {
            if (IsCborType(type))
            {
                // Add the type itself
                cborTypes.Add(type);

                // If it's a generic type, add its generic definition
                if (type.IsGenericType)
                {
                    cborTypes.Add(type.GetGenericTypeDefinition());
                }

                // Look through constructors
                foreach (var ctor in type.GetConstructors())
                {
                    foreach (var param in ctor.GetParameters())
                    {
                        var paramType = param.ParameterType;
                        if (IsCborType(paramType))
                        {
                            cborTypes.Add(paramType);
                            if (paramType.IsGenericType)
                            {
                                cborTypes.Add(paramType.GetGenericTypeDefinition());
                            }
                        }
                    }
                }

                // Look through properties
                foreach (var prop in type.GetProperties())
                {
                    var propType = prop.PropertyType;
                    if (IsCborType(propType))
                    {
                        cborTypes.Add(propType);
                        if (propType.IsGenericType)
                        {
                            cborTypes.Add(propType.GetGenericTypeDefinition());
                        }
                    }
                }
            }
        }

        var unionTypes = new Dictionary<Type, List<Type>>();

        // First pass: Identify union types and collect implementations
        foreach (var type in cborTypes)
        {
            var converterAttr = type.GetCustomAttribute<CborConverterAttribute>();
            if (converterAttr?.ConverterType == typeof(UnionConverter))
            {
                unionTypes[type] = [];
                Console.WriteLine($"Found union type: {type.FullName}");

                // Find all implementations
                var implementations = cborTypes
                    .Where(t => !t.IsAbstract)
                    .Where(t =>
                    {
                        if (type.IsGenericTypeDefinition)
                        {
                            // For generic unions, only get the generic definitions of implementations
                            if (!t.IsGenericType) return false;
                            return t.IsGenericTypeDefinition && IsGenericImplementationOf(t, type);
                        }
                        return type.IsAssignableFrom(t);
                    })
                    .ToList();

                unionTypes[type].AddRange(implementations);

                foreach (var impl in implementations)
                {
                    Console.WriteLine($"Found implementation {impl.FullName} for union {type.FullName}");
                }
            }
        }

        // Second pass: Register all types with their options
        foreach (var type in cborTypes)
        {
            // Get the registration type (open generic if it's a closed generic)
            var targetType = type.IsGenericType && !type.IsGenericTypeDefinition
                ? type.GetGenericTypeDefinition()
                : type;

            // Create options
            var options = CreateCborOptions(targetType);
            if (options != null)
            {
                // If this is a union type, add its implementations
                if (unionTypes.ContainsKey(targetType))
                {
                    options.UnionTypes = unionTypes[targetType];
                    Console.WriteLine($"Adding {unionTypes[targetType].Count} implementations to {targetType.FullName}");
                }

                _options.TryAdd(targetType, options);
            }

            // Register activator if it's not abstract
            if (!targetType.IsAbstract)
            {
                RegisterActivator(targetType);
            }
        }
    }

    private bool IsGenericImplementationOf(Type potentialImpl, Type baseType)
    {
        if (!baseType.IsGenericTypeDefinition || !potentialImpl.IsGenericTypeDefinition)
            return false;

        var currentType = potentialImpl;
        while (currentType != null && currentType != typeof(object))
        {
            if (currentType.IsGenericType)
            {
                var typeDefinition = currentType.GetGenericTypeDefinition();
                if (typeDefinition == baseType)
                    return true;
            }
            currentType = currentType.BaseType;
        }
        return false;
    }

    private bool IsCborType(Type type)
    {
        var current = type;
        while (current != null && current != typeof(object))
        {
            if (current == typeof(CborBase))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private CborOptions? CreateCborOptions(Type type)
    {
        Dictionary<string, Type> propertyNameTypes = [];
        Dictionary<int, Type> propertyIndexTypes = [];
        Type? converterType = null;
        bool? isDefinite = null;
        int? index = null;
        int? size = null;
        int? tag = null;

        // Walk up inheritance chain to gather attributes
        Type? currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            // Get converter
            CborConverterAttribute? converterAttr = currentType.GetCustomAttribute<CborConverterAttribute>();
            if (converterAttr != null && converterType == null)
            {
                converterType = converterAttr.ConverterType;
            }

            // Get other attributes
            CborIndexAttribute? indexAttr = currentType.GetCustomAttribute<CborIndexAttribute>();
            if (indexAttr != null && index == null)
            {
                index = indexAttr.Index;
            }

            CborTagAttribute? tagAttr = currentType.GetCustomAttribute<CborTagAttribute>();
            if (tagAttr != null && tag == null)
            {
                tag = tagAttr.Tag;
            }

            if (isDefinite == null)
            {
                if (currentType.GetCustomAttribute<CborDefiniteAttribute>() != null)
                    isDefinite = true;
                else if (currentType.GetCustomAttribute<CborIndefiniteAttribute>() != null)
                    isDefinite = false;
            }

            // Process properties and constructor parameters
            foreach (PropertyInfo prop in currentType.GetProperties())
            {
                CborPropertyAttribute? attr = prop.GetCustomAttribute<CborPropertyAttribute>();
                if (attr != null)
                {
                    if (attr.PropertyName != null)
                        propertyNameTypes[attr.PropertyName] = prop.PropertyType;
                    if (attr.Index.HasValue)
                        propertyIndexTypes[attr.Index.Value] = prop.PropertyType;
                }
            }

            foreach (ConstructorInfo ctor in currentType.GetConstructors())
            {
                foreach (ParameterInfo param in ctor.GetParameters())
                {
                    CborPropertyAttribute? attr = param.GetCustomAttribute<CborPropertyAttribute>();
                    if (attr != null)
                    {
                        if (attr.PropertyName != null)
                            propertyNameTypes[attr.PropertyName] = param.ParameterType;
                        if (attr.Index.HasValue)
                            propertyIndexTypes[attr.Index.Value] = param.ParameterType;
                    }
                }
            }

            currentType = currentType.BaseType;
        }

        if (converterType != null || isDefinite != null || index != null || tag != null ||
            propertyNameTypes.Any() || propertyIndexTypes.Any())
        {
            return new CborOptions(
                Index: index,
                ConverterType: converterType,
                IsDefinite: isDefinite,
                IsUnion: converterType == typeof(UnionConverter),
                ActivatorType: type,
                Size: size,
                Tag: tag,
                PropertyNameTypes: propertyNameTypes,
                PropertyIndexTypes: propertyIndexTypes
            );
        }

        return null;
    }

    private void RegisterActivator(Type type)
    {
        try
        {
            ConstructorInfo? ctor = type.GetConstructors().FirstOrDefault();
            if (ctor == null) return;

            ParameterInfo[] parameters = ctor.GetParameters();
            if (parameters.Length == 0)
            {
                LambdaExpression expr = Expression.Lambda(Expression.New(ctor));
                _activators.TryAdd(type, expr.Compile());
            }
            else
            {
                ParameterExpression[] paramExprs = parameters.Select(p =>
                    Expression.Parameter(p.ParameterType, p.Name)).ToArray();
                LambdaExpression expr = Expression.Lambda(Expression.New(ctor, paramExprs), paramExprs);
                _activators.TryAdd(type, expr.Compile());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create activator for {type.FullName}: {ex.Message}");
        }
    }

    // Helper method to get normalized type (converts closed generic to open generic)
    public Type NormalizeType(Type type)
    {
        return type.IsGenericType && !type.IsGenericTypeDefinition
            ? type.GetGenericTypeDefinition()
            : type;
    }

    // The rest of your registry implementation...
    private void RegisterGettersAndSetters()
    {
        // Only process non-generic and closed generic types
        IEnumerable<Type> validTypes = _options.Keys
            .Where(t => !t.IsGenericTypeDefinition)
            .Where(t => !t.ContainsGenericParameters);

        foreach (Type? type in validTypes)
        {
            foreach (PropertyInfo prop in type.GetProperties())
            {
                try
                {
                    // Getter
                    if (prop.CanRead)
                    {
                        ParameterExpression instanceParam = Expression.Parameter(type, "instance");
                        MemberExpression property = Expression.Property(instanceParam, prop);
                        LambdaExpression lambda = Expression.Lambda(property, instanceParam);
                        _getters.TryAdd((type, prop.Name), lambda.Compile());
                    }

                    // Setter
                    if (prop.CanWrite)
                    {
                        ParameterExpression instanceParam = Expression.Parameter(type, "instance");
                        ParameterExpression valueParam = Expression.Parameter(prop.PropertyType, "value");
                        BinaryExpression assign = Expression.Assign(Expression.Property(instanceParam, prop), valueParam);
                        LambdaExpression lambda = Expression.Lambda(assign, instanceParam, valueParam);
                        _setters.TryAdd((type, prop.Name), lambda.Compile());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to create getter/setter for {type.FullName}.{prop.Name}: {ex.Message}");
                }
            }
        }
    }

    private void RegisterPropertyInfo()
    {
        foreach (Type type in _options.Keys)
        {
            PropertyInfo[] props = type.GetProperties()
                .OrderBy(p => p.GetCustomAttribute<CborPropertyAttribute>()?.Index ?? int.MaxValue)
                .ToArray();
            _properties.TryAdd(type, props);
        }
    }

    private void RegisterConstructors()
    {
        foreach (Type type in _options.Keys)
        {
            ConstructorInfo[] ctors = type.GetConstructors();
            _constructors.TryAdd(type, ctors);
        }
    }

    private void RegisterMethods()
    {
        foreach (Type type in _options.Keys)
        {
            IEnumerable<MethodInfo> methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType == type);
            foreach (MethodInfo? method in methods)
            {
                _methods.TryAdd((type, method.Name), method);
            }
        }
    }
}
