using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Utils;

public static class ActivatorUtil
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object CreateInstance(Type targetType, object? value, CborOptions options)
    {
        ConstructorInfo ctor = targetType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault() ?? throw new InvalidOperationException($"No constructor found for {targetType}");

        ParameterInfo[] parameters = ctor.GetParameters();

        object result = parameters.Length switch
        {
            0 => Activator.CreateInstance(targetType)!,
            1 => CreateSingleParameterInstance(targetType, ctor, value),
            _ => CreateMultiParameterInstance(targetType, ctor, value, options)
        };

        return result;
    }

    private static object CreateSingleParameterInstance(Type targetType, ConstructorInfo ctor, object? value)
    {
        Type paramType = ctor.GetParameters()[0].ParameterType;

        if (value != null && IsCollectionType(paramType))
        {
            object convertedValue = ConvertCollection(value, paramType);
            return Activator.CreateInstance(targetType, convertedValue)!;
        }

        return Activator.CreateInstance(targetType, value)!;
    }

    private static object CreateMultiParameterInstance(Type targetType, ConstructorInfo ctor, object? value, CborOptions options)
    {
        ParameterInfo[] parameters = ctor.GetParameters();
        object?[] args = new object?[parameters.Length];

        switch (value)
        {
            case Dictionary<object, object> dict:
                MapDictionaryToParameters(parameters, dict, args, options);
                break;
            case IEnumerable<object> sequence:
                MapSequenceToParameters(sequence, args);
                break;
        }

        return Activator.CreateInstance(targetType, args)!;
    }

    private static void MapDictionaryToParameters(
        ParameterInfo[] parameters,
        Dictionary<object, object> dict,
        object?[] args,
    CborOptions options)
    {
        if (options.IndexPropertyMapping != null)
        {
            int currPropIndex = 0;
            foreach (KeyValuePair<int, Type> mapping in options.IndexPropertyMapping)
            {
                int mappingKey = mapping.Key;
                object? foundValue = dict
                    .Where(kv => TryConvertKeyToInt(kv.Key, out int keyAsInt) && keyAsInt == mappingKey)
                    .Select(kv => kv.Value)
                    .FirstOrDefault();
                args[currPropIndex++] = foundValue;
            }
            return;
        }

        // Handle name-based mapping
        if (options.NamedPropertyMapping != null)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo param = parameters[i];
                CborPropertyAttribute? propAttr = param.GetCustomAttribute<CborPropertyAttribute>();

                if (propAttr != null && dict.TryGetValue(propAttr.Name, out object? value))
                {
                    args[i] = value;
                }
            }
            return;
        }

        // Handle regular dictionary case (MapConverter)
        if (parameters.Length == 1)
        {
            Type paramType = parameters[0].ParameterType;
            if (paramType.IsGenericType &&
                paramType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                Type[] genericArgs = paramType.GetGenericArguments();
                Type dictType = typeof(Dictionary<,>).MakeGenericType(genericArgs);
                IDictionary typedDict = (IDictionary)Activator.CreateInstance(dictType)!;

                foreach (KeyValuePair<object, object> kvp in dict)
                {
                    typedDict.Add(kvp.Key, kvp.Value);
                }

                args[0] = typedDict;
            }
        }
    }

    private static bool TryConvertKeyToInt(object key, out int result)
    {
        if (key is int i)
        {
            result = i;
            return true;
        }
        try
        {
            result = Convert.ToInt32(key);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static void MapSequenceToParameters(IEnumerable<object> sequence, object?[] args)
    {
        object[] values = [.. sequence];
        Array.Copy(values, args, Math.Min(values.Length, args.Length));
    }

    private static bool IsCollectionType(Type type)
    {
        if (!type.IsGenericType)
            return false;

        Type genericType = type.GetGenericTypeDefinition();
        return genericType == typeof(List<>) ||
               genericType == typeof(Dictionary<,>);
    }

    private static object ConvertCollection(object value, Type targetType)
    {
        if (!targetType.IsGenericType)
            throw new InvalidOperationException($"Expected generic collection type, got {targetType}");

        Type genericDef = targetType.GetGenericTypeDefinition();

        return genericDef switch
        {
            var t when t == typeof(List<>) => CreateGenericList(value, targetType.GetGenericArguments()[0]),
            var t when t == typeof(Dictionary<,>) => CreateGenericDictionary(value, targetType.GetGenericArguments()),
            _ => throw new InvalidOperationException($"Unsupported collection type: {targetType}")
        };
    }

    private static object CreateGenericList(object value, Type elementType)
    {
        IList list = (IList)Activator.CreateInstance(typeof(List<>)
            .MakeGenericType(elementType))!;

        if (value is IEnumerable<object> items)
        {
            foreach (object item in items)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static object CreateGenericDictionary(object value, Type[] typeArgs)
    {
        IDictionary dict = (IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeArgs))!;

        switch (value)
        {
            case IEnumerable<KeyValuePair<object, object?>> entries:
                foreach (KeyValuePair<object, object?> entry in entries)
                {
                    if (!dict.Contains(entry.Key))
                    {
                        dict.Add(entry.Key, entry.Value);
                    }
                }
                break;

            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!dict.Contains(entry.Key))
                    {
                        dict.Add(entry.Key, entry.Value);
                    }
                }
                break;

            case IEnumerable enumerable:
                foreach (object? item in enumerable)
                {
                    if (item is KeyValuePair<object, object> kvp)
                    {
                        if (!dict.Contains(kvp.Key))
                        {
                            dict.Add(kvp.Key, kvp.Value);
                        }
                    }
                }
                break;
        }

        return dict;
    }
}