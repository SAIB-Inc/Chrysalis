using System.Reflection;
using Chrysalis.Cbor.Attributes;

namespace Chrysalis.Cbor.Utils;

public static class PropertyResolvers
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
}