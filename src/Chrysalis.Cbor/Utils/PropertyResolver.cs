using System.Collections;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Utils;

public static class PropertyResolver
{
    public static (
        IReadOnlyDictionary<int, (Type Type, object? ExpectedValue)> IndexMap, 
        IReadOnlyDictionary<string, (Type Type, object? ExpectedValue)> NamedMap, 
        ConstructorInfo Constructor
    ) ResolvePropertyMappings(Type type)
    {
        if (type.IsAbstract)
            return (new Dictionary<int, (Type, object?)>(), new Dictionary<string, (Type, object?)>(), null!);

        ConstructorInfo constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No suitable constructor found for {type}");

        ParameterInfo[] parameters = constructor.GetParameters();
        Dictionary<int, (Type Type, object? ExpectedValue)> indexMap = [];
        Dictionary<string, (Type Type, object? ExpectedValue)> namedMap = [];

        foreach (ParameterInfo param in parameters)
        {
            // Retrieve CBOR attributes
            CborIndexAttribute? indexAttr = param.GetCustomAttribute<CborIndexAttribute>();
            CborPropertyAttribute? propAttr = param.GetCustomAttribute<CborPropertyAttribute>();
            // Retrieve ExactValue attribute
            ExactValueAttribute? exactAttr = param.GetCustomAttribute<ExactValueAttribute>();

            // Map index if present
            if (indexAttr != null)
            {
                indexMap[indexAttr.Index] = (param.ParameterType, exactAttr?.Value);
                continue;
            }

            // Map property name if present
            if (propAttr != null)
            {
                namedMap[propAttr.Name] = (param.ParameterType, exactAttr?.Value);
            }
        }

        return (indexMap, namedMap, constructor);
    }

    // Other methods remain unchanged for this query but are included for completeness
    public static List<object?> GetPropertyValues(object obj, ConstructorInfo constructorInfo)
    {
        ParameterInfo[] parameters = constructorInfo.GetParameters();
        List<object?> values = [.. new object?[parameters.Length]];

        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo param = parameters[i];
            PropertyInfo? property = obj.GetType().GetProperty(param.Name!,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property != null)
            {
                values[i] = property.GetValue(obj);
            }
        }

        return values;
    }

    public static List<object?> GetObjectProperties(object obj)
    {
        Type type = obj.GetType();
        ConstructorInfo? constructor = type.GetConstructors().FirstOrDefault();
        if (constructor == null)
            return [];

        List<object?> results = [];
        foreach (ParameterInfo parameter in constructor.GetParameters())
        {
            bool hasAttribute = parameter.GetCustomAttribute<CborIndexAttribute>() != null ||
                                parameter.GetCustomAttribute<CborPropertyAttribute>() != null;
            if (hasAttribute)
            {
                PropertyInfo? property = type.GetProperty(parameter.Name!);
                if (property is not null)
                {
                    object? val = property.GetValue(obj);
                    if (val is not null)
                        results.Add(val);
                }
            }
        }

        return results;
    }

    public static List<object?> GetFilteredProperties(object obj)
    {
        return obj.GetType()
            .GetProperties()
            .Where(p => p.Name != "Raw")
            .Where(p => p is not null)
            .Select(p => p.GetValue(obj))
            .ToList();
    }

    public static Type GetInnerType(CborOptions options, object? value)
    {
        // 1. Try to extract from generic type arguments first
        if (options.RuntimeType?.IsGenericType == true)
        {
            Type[] args = options.RuntimeType.GetGenericArguments();
            if (args.Length > 0)
                return args[0];
        }

        // 2. Try to get from constructor parameter
        Type? paramType = options.RuntimeType
            ?.GetConstructors().FirstOrDefault()
            ?.GetParameters().FirstOrDefault()
            ?.ParameterType;

        if (paramType?.IsGenericType == true)
        {
            Type[] genericArgs = paramType.GetGenericArguments();
            if (genericArgs.Length > 0)
                return genericArgs[0];
        }

        // 3. For collections of simple types, use dynamic type detection from values
        if (value != null)
        {
            if (value is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (item != null)
                        return item.GetType();
                }
            }
            else if (value is IList<object?> list && list.Count > 0)
            {
                object? firstNonNull = list.FirstOrDefault(x => x != null);
                if (firstNonNull != null)
                    return firstNonNull.GetType();
            }
        }

        // 4. Last resort: use object type (least specific)
        return typeof(object);
    }
}