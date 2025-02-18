using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Utils;

internal static class OptionsUtils
{
    public static CborOptions CreateOptions(Type type, Dictionary<Type, List<Type>> unionTypes)
    {
        var optionsStack = new Stack<(
            Type? converterType,
            bool? isDefinite,
            int? index,
            int? tag,
            Dictionary<string, Type> propertyNameTypes,
            Dictionary<int, Type> propertyIndexTypes
        )>();

        Type? currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            var converterAttr = currentType.GetCustomAttribute<CborConverterAttribute>();
            var indexAttr = currentType.GetCustomAttribute<CborIndexAttribute>();
            var tagAttr = currentType.GetCustomAttribute<CborTagAttribute>();

            // Fix the conditional expression
            bool? isDefinite = null;
            if (currentType.GetCustomAttribute<CborDefiniteAttribute>() != null)
                isDefinite = true;
            else if (currentType.GetCustomAttribute<CborIndefiniteAttribute>() != null)
                isDefinite = false;

            var propertyNameTypes = new Dictionary<string, Type>();
            var propertyIndexTypes = new Dictionary<int, Type>();

            // Process properties and constructor parameters
            ProcessMembers(currentType, propertyNameTypes, propertyIndexTypes);

            optionsStack.Push((
                converterAttr?.ConverterType,
                isDefinite,
                indexAttr?.Index,
                tagAttr?.Tag,
                propertyNameTypes,
                propertyIndexTypes
            ));

            currentType = currentType.BaseType;
        }

        // Merge options, giving priority to derived types
        Type? finalConverterType = null;
        bool? finalIsDefinite = null;
        int? finalIndex = null;
        int? finalTag = null;
        var finalPropertyNameTypes = new Dictionary<string, Type>();
        var finalPropertyIndexTypes = new Dictionary<int, Type>();

        while (optionsStack.Count > 0)
        {
            var (converterType, isDefinite, index, tag, propertyNameTypes, propertyIndexTypes) = optionsStack.Pop();

            finalConverterType ??= converterType;
            finalIsDefinite ??= isDefinite;
            finalIndex ??= index;
            finalTag ??= tag;

            foreach (var (key, value) in propertyNameTypes)
                if (!finalPropertyNameTypes.ContainsKey(key))
                    finalPropertyNameTypes[key] = value;

            foreach (var (key, value) in propertyIndexTypes)
                if (!finalPropertyIndexTypes.ContainsKey(key))
                    finalPropertyIndexTypes[key] = value;
        }

        return new CborOptions(
            Index: finalIndex,
            ConverterType: finalConverterType,
            IsDefinite: finalIsDefinite,
            IsUnion: finalConverterType == typeof(UnionConverter),
            ActivatorType: type,
            Size: null,
            Tag: finalTag,
            PropertyNameTypes: finalPropertyNameTypes,
            PropertyIndexTypes: finalPropertyIndexTypes,
            UnionTypes: unionTypes.GetValueOrDefault(type)
        );
    }

    private static void ProcessMembers(
        Type type,
        Dictionary<string, Type> propertyNameTypes,
        Dictionary<int, Type> propertyIndexTypes)
    {
        foreach (var prop in type.GetProperties())
        {
            var attr = prop.GetCustomAttribute<CborPropertyAttribute>();
            if (attr != null)
            {
                if (attr.PropertyName != null)
                    propertyNameTypes[attr.PropertyName] = prop.PropertyType;
                if (attr.Index.HasValue)
                    propertyIndexTypes[attr.Index.Value] = prop.PropertyType;
            }
        }

        foreach (var ctor in type.GetConstructors())
        {
            foreach (var param in ctor.GetParameters())
            {
                var attr = param.GetCustomAttribute<CborPropertyAttribute>();
                if (attr != null)
                {
                    if (attr.PropertyName != null)
                        propertyNameTypes[attr.PropertyName] = param.ParameterType;
                    if (attr.Index.HasValue)
                        propertyIndexTypes[attr.Index.Value] = param.ParameterType;
                }
            }
        }
    }

    private static bool? GetDefiniteAttribute(Type type) =>
        type.GetCustomAttribute<CborDefiniteAttribute>() != null ? true :
        type.GetCustomAttribute<CborIndefiniteAttribute>() != null ? false : null;

    private static Dictionary<string, Type> CollectPropertyNames(Type type) =>
        type.GetProperties()
            .Select(p => (Prop: p, Attr: p.GetCustomAttribute<CborPropertyAttribute>()))
            .Where(x => x.Attr?.PropertyName != null)
            .ToDictionary(x => x.Attr!.PropertyName!, x => x.Prop.PropertyType);

    private static Dictionary<int, Type> CollectPropertyIndices(Type type) =>
        type.GetProperties()
            .Select(p => (Prop: p, Attr: p.GetCustomAttribute<CborPropertyAttribute>()))
            .Where(x => x.Attr?.Index != null)
            .ToDictionary(x => x.Attr!.Index!.Value, x => x.Prop.PropertyType);
}