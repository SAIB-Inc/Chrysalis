using System.Collections;
using System.Collections.Concurrent;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cache;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public static class CborSerializer
{
    private static ChrysalisRegistry? _registry;
    private static ChrysalisRegistry Registry
    {
        get
        {
            if (_registry == null)
            {
                _registry = new ChrysalisRegistry();
                _registry.InitializeRegistry();
            }
            return _registry;
        }
        set => _registry = value;
    }

    public static byte[] Serialize<T>(T value) where T : CborBase
    {
        throw new NotImplementedException();
    }

    public static T Deserialize<T>(byte[] data) where T : CborBase
    {
        Type type = typeof(T);
        CborReader reader = new(data);
        CborOptions? options = Registry.GetOptions(type)
            ?? throw new InvalidOperationException($"No options registered for type {type}");

        // The internal method now instantiates the object.
        T instance = (T)Deserialize(reader, options)!;
        instance.Raw = data;
        return instance;
    }

    internal static object? Deserialize(CborReader reader, CborOptions? options = null)
    {
        ICborConverter converter = Registry.GetConverter(options?.ConverterType ?? throw new InvalidOperationException("No converter type"));

        object? result = converter.Deserialize(reader, options);
        if (result == null) return null;

        Type activatorType = options?.ActivatorType ?? throw new InvalidOperationException("No activator type specified");
        var bindingFlags = activatorType.IsGenericType && !activatorType.IsGenericTypeDefinition
            ? BindingFlags.Public | BindingFlags.Instance
            : BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        ConstructorInfo? ctor = activatorType.GetConstructors(bindingFlags)
              .OrderByDescending(c => c.GetParameters().Length)
              .FirstOrDefault();

        if (ctor is null)
        {
            throw new InvalidOperationException($"No constructor found for type {activatorType.FullName}");
        }

        int paramCount = ctor.GetParameters().Length;

        Console.WriteLine($"Type: {activatorType.Name}");
        Console.WriteLine($"Parameter Count: {paramCount}");
        Console.WriteLine($"Result type: {result?.GetType()}");

        if (activatorType.Name == "AlonzoTransactionBody")
        {
            Console.WriteLine("here");
        }

        if (result != null)
        {
            Console.WriteLine($"Result value: {result}");
        }

        var parameters = ctor.GetParameters();

        if (result is Dictionary<object, object> dict && paramCount > 0)
        {
            var args = new object?[paramCount];

            // Map dictionary values to constructor parameters using CBOR property indices
            foreach (var param in parameters)
            {
                var cborProp = param.GetCustomAttribute<CborPropertyAttribute>();
                if (cborProp != null)
                {
                    // Try to get by index
                    if (dict.ContainsKey(cborProp.Index))
                    {
                        args[param.Position] = dict[cborProp.Index];
                    }
                    // If value not found, it's an optional parameter
                    else
                    {
                        args[param.Position] = null;
                    }
                }
            }

            return Activator.CreateInstance(activatorType, args);
        }

        if (paramCount == 1)
        {
            var paramType = ctor.GetParameters()[0].ParameterType;
            Console.WriteLine($"Parameter type: {paramType}");
            Console.WriteLine($"Result type: {result?.GetType()}");

            // Handle Dictionary types
            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                && result is List<KeyValuePair<object, object?>> entries)
            {
                var dictTypes = paramType.GetGenericArguments();
                var newDict = (IDictionary)Activator.CreateInstance(
                    typeof(Dictionary<,>).MakeGenericType(dictTypes[0], dictTypes[1]))!;

                foreach (var entry in entries)
                {
                    newDict.Add(entry.Key, entry.Value);
                }
                return Activator.CreateInstance(activatorType, newDict);
            }

            // Handle List types
            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(List<>)
                && result is IEnumerable<object> enumerable)
            {
                var listType = paramType.GetGenericArguments()[0];
                var listz = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(listType))!;
                foreach (var item in enumerable)
                {
                    listz.Add(item);
                }
                return Activator.CreateInstance(activatorType, listz);
            }

            return Activator.CreateInstance(activatorType, result);
        }

        // No parameters? Just create instance
        if (paramCount == 0)
            return Activator.CreateInstance(activatorType);

        // Multiple parameters? Convert result to array
        if (paramCount > 1)
        {
            if (result is IEnumerable<object> list)
            {
                var args = list.ToList();
                // Pad with nulls if we have fewer values than parameters
                while (args.Count < paramCount)
                {
                    args.Add(null);
                }
                return Activator.CreateInstance(activatorType, args.ToArray());
            }
        }

        // Single parameter (fallback)
        return Activator.CreateInstance(activatorType, result);
    }

    internal static void Serialize(CborWriter writer, object? value)
    {
        throw new NotImplementedException();
    }

    internal static object[] GetPropertyValues(object value, int index)
    {
        Type type = value.GetType();
        PropertyInfo[] properties = Registry.GetProperties(type);
        object[] values = new object[properties.Length];
        for (int i = 0; i < properties.Length; i++)
        {
            Delegate getter = Registry.GetGetter(type, properties[i].Name);
            values[i] = getter.DynamicInvoke(value)!;
        }
        return values;
    }

    internal static object[] GetSortedProperties(object value)
    {
        Type type = value.GetType();
        PropertyInfo[] properties = Registry.GetProperties(type);
        object[] values = new object[properties.Length];
        for (int i = 0; i < properties.Length; i++)
        {
            Delegate getter = Registry.GetGetter(type, properties[i].Name);
            object? propValue = getter.DynamicInvoke(value);
            if (propValue != null)
            {
                values[i] = propValue;
            }
        }
        return values;
    }

    internal static Dictionary<object, object> GetPropertyMappings(object value)
    {
        Type type = value.GetType();
        PropertyInfo[] properties = Registry.GetProperties(type);
        Dictionary<object, object> mappings = [];
        foreach (PropertyInfo prop in properties)
        {
            Delegate getter = Registry.GetGetter(type, prop.Name);
            object? propValue = getter.DynamicInvoke(value);
            if (propValue != null)
            {
                object key = GetPropertyKey(type, prop.Name);
                mappings[key] = propValue;
            }
        }
        return mappings;
    }

    private static object GetPropertyKey(Type type, string propertyName)
    {
        CborOptions? options = Registry.GetOptions(type);
        PropertyInfo[] properties = Registry.GetProperties(type);
        PropertyInfo property = properties.First(p => p.Name == propertyName);
        if (options?.PropertyNameTypes?.TryGetValue(propertyName, out _) == true)
        {
            return propertyName;
        }
        foreach (KeyValuePair<int, Type> kvp in options?.PropertyIndexTypes ?? [])
        {
            if (properties[kvp.Key].Name == propertyName)
            {
                return kvp.Key; // Return the index
            }
        }
        return propertyName.ToLowerInvariant();
    }

    internal static Type[] GetConcreteTypes(Type baseType)
    {
        // This would use the registry's cached type information
        Type[] types = [.. Registry.GetOptions().Keys.Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract)];
        return types;
    }

    internal static object? TryDeserialize(byte[] data, Type type)
    {
        CborOptions option = Registry.GetOptions(type);
        option.ActivatorType = type;
        ICborConverter converter = Registry.GetConverter(option.ConverterType!);
        CborReader reader = new(data);
        return converter.Deserialize(reader, option);
    }

    internal static CborOptions? GetOptions(Type type)
    {
        return Registry.GetOptions(type);
    }

    internal static Delegate GetActivator(Type type)
    {
        return Registry.GetActivator(type);
    }

    internal static ICborConverter GetConverter(Type type)
    {
        return Registry.GetConverter(type);
    }
}