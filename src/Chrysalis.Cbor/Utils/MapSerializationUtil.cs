using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Utils;

public static class MapSerializationUtil
{

    public static List<KeyValuePair<object, object?>> ReadKeyValuePairs(CborReader reader, CborOptions options)
    {
        List<KeyValuePair<object, object?>> entries = [];
        HashSet<object> seenKeys = [];
        (Type KeyType, Type ValueType) genericTypes = GetGenericMapTypes(options);

        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            object? key = genericTypes.KeyType.TryCallStaticRead(reader);
            object? value = genericTypes.ValueType.TryCallStaticRead(reader);

            if (key != null && seenKeys.Add(key))  // Add returns false if key already exists
                entries.Add(new KeyValuePair<object, object?>(key, value));
        }
        reader.ReadEndMap();

        return entries;
    }

    public static (Type KeyType, Type ValueType) GetGenericMapTypes(CborOptions options)
    {
        try
        {
            // If RuntimeType is available, use it directly
            if (options.RuntimeType != null)
            {
                return ExtractDictionaryTypes(options.RuntimeType);
            }

            // Try ObjectType next
            if (options.ObjectType != null)
            {
                return ExtractDictionaryTypes(options.ObjectType);
            }

            // Fall back to constructor-based extraction
            if (options.Constructor != null)
            {
                ParameterInfo? parameter = options.Constructor.GetParameters()
                    .FirstOrDefault(p => p.ParameterType.IsGenericType &&
                                   p.ParameterType.GetGenericTypeDefinition() == typeof(Dictionary<,>));

                if (parameter != null)
                {
                    Type[] genericArgs = parameter.ParameterType.GetGenericArguments();
                    return (genericArgs[0], genericArgs[1]);
                }
            }

            throw new InvalidOperationException($"Cannot determine generic types from options");
        }
        catch (Exception ex)
        {
            string typeName = options.RuntimeType?.Name ?? options.ObjectType?.Name ?? "unknown";
            throw new InvalidOperationException(
                $"Cannot determine dictionary types for {typeName}", ex);
        }
    }

    private static (Type KeyType, Type ValueType) ExtractDictionaryTypes(Type type)
    {
        // For direct Dictionary<K,V> type
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            Type[] args = type.GetGenericArguments();
            return (args[0], args[1]);
        }

        // For types that implement IDictionary<K,V>
        foreach (Type interfaceType in type.GetInterfaces())
        {
            if (interfaceType.IsGenericType &&
                interfaceType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                Type[] args = interfaceType.GetGenericArguments();
                return (args[0], args[1]);
            }
        }

        // For types with a Dictionary<K,V> property - works for record types like MultiAssetOutput
        PropertyInfo? propertyWithDictionary = type.GetProperties()
            .FirstOrDefault(p => p.PropertyType.IsGenericType &&
                          p.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>));

        if (propertyWithDictionary != null)
        {
            Type[] args = propertyWithDictionary.PropertyType.GetGenericArguments();
            return (args[0], args[1]);
        }

        // For record types with Dictionary constructor parameter
        ParameterInfo? constructorWithDictionary = type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .FirstOrDefault(p => p.ParameterType.IsGenericType &&
                           p.ParameterType.GetGenericTypeDefinition() == typeof(Dictionary<,>));

        if (constructorWithDictionary != null)
        {
            Type[] args = constructorWithDictionary.ParameterType.GetGenericArguments();
            return (args[0], args[1]);
        }

        throw new InvalidOperationException($"Type {type.Name} is not a Dictionary<,> or IDictionary<,>");
    }

    public static object CreateMapInstance(List<KeyValuePair<object, object?>> entries, CborOptions options)
    {
        try
        {
            // Check if we're dealing with CborMap directly
            bool isCborMap = options.ObjectType?.Name?.StartsWith("CborMap`2") == true ||
                             options.RuntimeType?.Name?.StartsWith("CborMap`2") == true;

            if (isCborMap && entries.Count > 0)
            {
                // For CborMap, directly use concrete types from the first entry
                Type keyType = entries[0].Key?.GetType() ?? typeof(object);
                Type valueType = entries[0].Value?.GetType() ?? typeof(object);

                // Create the CborMap with these concrete types
                Type cborMapType = typeof(Chrysalis.Cbor.Types.Primitives.CborMap<,>)
                    .MakeGenericType(keyType, valueType);

                // Create dictionary with the same concrete types
                Type dType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                IDictionary d = (IDictionary)Activator.CreateInstance(dType)!;

                // Add entries
                foreach (KeyValuePair<object, object?> entry in entries)
                {
                    d.Add(entry.Key, entry.Value);
                }

                // Create the instance
                return Activator.CreateInstance(cborMapType, d)!;
            }

            // Standard approach for other types
            (Type KeyType, Type ValueType) genericTypes = GetGenericMapTypes(options);

            // Create dictionary
            Type dictType = typeof(Dictionary<,>).MakeGenericType(genericTypes.KeyType, genericTypes.ValueType);
            IDictionary dict = (IDictionary)Activator.CreateInstance(dictType)!;

            // Add entries
            foreach (KeyValuePair<object, object?> entry in entries)
            {
                dict.Add(entry.Key, entry.Value);
            }

            // Invoke constructor
            return options.Constructor!.Invoke([dict]);
        }
        catch (Exception ex)
        {
            string typeName = options.RuntimeType?.Name ?? options.ObjectType?.Name ?? "unknown";
            throw new InvalidOperationException(
                $"Failed to create map instance for {typeName}: {ex.Message}", ex);
        }
    }
}