using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Attributes;

namespace Chrysalis.Cbor.Utils;

public static class PropertyResolver
{
    public static (IReadOnlyDictionary<int, Type> IndexMap, IReadOnlyDictionary<string, Type> NamedMap, ConstructorInfo Constructor) ResolvePropertyMappings(Type type)
    {
        if (type.IsAbstract)
            return (new Dictionary<int, Type>(), new Dictionary<string, Type>(), null!);

        ConstructorInfo constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No suitable constructor found for {type}");

        ParameterInfo[] parameters = constructor.GetParameters();
        Dictionary<int, Type> indexMap = [];
        Dictionary<string, Type> namedMap = [];

        foreach (ParameterInfo param in parameters)
        {
            CborIndexAttribute? indexAttr = param.GetCustomAttribute<CborIndexAttribute>();
            if (indexAttr != null)
            {
                indexMap[indexAttr.Index] = param.ParameterType;
                continue;
            }

            CborPropertyAttribute? propAttr = param.GetCustomAttribute<CborPropertyAttribute>();

            if (propAttr != null)
                namedMap[propAttr.Name] = param.ParameterType;
        }

        return (indexMap, namedMap, constructor);
    }

    public static List<object?> GetPropertyValues(object obj, ConstructorInfo constructorInfo)
    {
        ParameterInfo[] parameters = constructorInfo.GetParameters();
        List<object?> values = [];

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
        return [.. obj.GetType()
            .GetProperties()
            .Where(p => p.Name != "Raw")
            .Select(p => p.GetValue(obj))];
    }
}